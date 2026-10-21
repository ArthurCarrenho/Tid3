<#
.SYNOPSIS
    Build, register and package Tid3 without Visual Studio.

.DESCRIPTION
    Everything WinUI 3 needs to build comes from NuGet: Microsoft.WindowsAppSDK
    provides the XAML compiler and the MSIX targets, Microsoft.Windows.SDK.BuildTools
    provides makeappx/makepri/signtool. The only prerequisite is the .NET 8 SDK.

    Tasks:
      dev         Build Debug, register the loose AppX layout, launch the app.
                  This is the fast inner loop: no MSIX packing, no signing.
      build       Build only, no register/launch.
      run         Launch the currently registered package.
      install     Build Release, copy it to %LOCALAPPDATA%\Programs\Tid3 and register
                  it from there: an install that no longer depends on bin\. Keeps the
                  app data (sign-in, queue, settings). 'dev' and 'install' share the
                  package identity, so whichever ran last is the registered one.
      msix        Publish Release and produce a signed .msix under packages\.
      unregister  Remove the registered package from this user (and its app data).
      clean       Delete bin\, obj\ and packages\.

.EXAMPLE
    .\build.ps1 dev
    .\build.ps1 dev -Platform arm64
    .\build.ps1 install
    .\build.ps1 msix -Platform x64
#>
[CmdletBinding()]
param(
    [Parameter(Position = 0)]
    [ValidateSet('dev', 'build', 'run', 'install', 'msix', 'unregister', 'clean')]
    [string]$Task = 'dev',

    [ValidateSet('x64', 'x86', 'arm64')]
    [string]$Platform = 'x64',

    [ValidateSet('Debug', 'Release')]
    [string]$Configuration,

    # Skip launching the app after 'dev'.
    [switch]$NoLaunch
)

$ErrorActionPreference = 'Stop'

$Root       = $PSScriptRoot
$Project    = Join-Path $Root 'TidalUi3.csproj'
$Rid        = @{ 'x64' = 'win-x64'; 'x86' = 'win-x86'; 'arm64' = 'win-arm64' }[$Platform]
$InstallDir = Join-Path $env:LOCALAPPDATA 'Programs\Tid3'

if (-not $Configuration) {
    $Configuration = if ($Task -in 'msix', 'install') { 'Release' } else { 'Debug' }
}

function Write-Step($message) {
    Write-Host "==> $message" -ForegroundColor Cyan
}

function Assert-DotnetSdk {
    if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
        throw "dotnet nao encontrado no PATH. Instale o .NET 8 SDK: https://dotnet.microsoft.com/download/dotnet/8.0"
    }
    $sdks = & dotnet --list-sdks 2>$null
    if (-not $sdks) {
        throw "Nenhum .NET SDK instalado (apenas runtimes). Instale o .NET 8 SDK x64: https://dotnet.microsoft.com/download/dotnet/8.0"
    }
    if (-not ($sdks | Where-Object { $_ -like '8.*' })) {
        Write-Warning "Nenhum SDK 8.x encontrado. global.json exige 8.0.1xx ou superior e o build vai falhar."
    }
}

function Get-Manifest {
    [xml](Get-Content (Join-Path $Root 'Package.appxmanifest'))
}

function Find-AppxLayout {
    # Retorna o caminho do AppxManifest.xml do layout solto, ou $null.
    $searchRoot = Join-Path $Root "bin\$Platform\$Configuration"
    if (-not (Test-Path $searchRoot)) { return $null }
    $manifest = Get-ChildItem -Path $searchRoot -Recurse -Filter 'AppxManifest.xml' -ErrorAction SilentlyContinue |
                Sort-Object LastWriteTime -Descending |
                Select-Object -First 1
    if ($manifest) { return $manifest.FullName }
    return $null
}

function Get-InstalledPackage {
    $identityName = (Get-Manifest).Package.Identity.Name
    Get-AppxPackage -Name $identityName -ErrorAction SilentlyContinue
}

function Stop-RunningApp([string]$fromDir = (Join-Path $Root 'bin')) {
    # O pacote registrado roda direto da pasta do layout, entao uma instancia aberta trava
    # o TidalUi3.exe: o build falha com MSB3021/MSB3027 e a copia do 'install' tambem.
    # So encerra instancias que rodam a partir de $fromDir -- uma versao instalada via
    # MSIX fica intacta.
    $procs = @(Get-Process -Name 'TidalUi3' -ErrorAction SilentlyContinue |
               Where-Object { $_.Path -and $_.Path.StartsWith($fromDir, [StringComparison]::OrdinalIgnoreCase) })

    foreach ($proc in $procs) {
        Write-Step "Encerrando Tid3 em execucao (PID $($proc.Id)) para liberar o executavel"
        # Fecha pela janela primeiro: o app persiste sessao/estado ao sair.
        $proc.CloseMainWindow() | Out-Null
        if (-not $proc.WaitForExit(5000)) {
            Write-Warning "  nao respondeu ao fechamento; encerrando a forca"
            $proc.Kill()
            $proc.WaitForExit(5000) | Out-Null
        }
    }
}

function Invoke-Build {
    Assert-DotnetSdk
    Stop-RunningApp
    Write-Step "Build $Configuration | $Platform ($Rid)"
    & dotnet build $Project -c $Configuration -p:Platform=$Platform -p:RuntimeIdentifier=$Rid
    if ($LASTEXITCODE -ne 0) { throw "dotnet build falhou (exit $LASTEXITCODE)." }
}

function Sync-AppxLayout($manifestPath) {
    # 'dotnet build' nao copia itens <Content> (os Assets\) para a pasta de saida:
    # o empacotamento MSIX le esses arquivos direto da origem via GetPackagingOutputs.
    # Como o -Register usa a pasta de saida como raiz do pacote, sem esta sincronia
    # o app instala sem icones. O .appxrecipe e a lista autoritativa do conteudo do
    # pacote e traz origem + destino de cada arquivo.
    $layoutRoot = Split-Path $manifestPath -Parent
    $recipe = Get-ChildItem -Path $layoutRoot -Filter '*.build.appxrecipe' -ErrorAction SilentlyContinue |
              Select-Object -First 1
    if (-not $recipe) {
        Write-Warning 'appxrecipe nao encontrado; o layout pode ficar incompleto (icones ausentes).'
        return
    }

    $ns    = @{ m = 'http://schemas.microsoft.com/developer/msbuild/2003' }
    $nodes = Select-Xml -Path $recipe.FullName -Namespace $ns -XPath '//m:AppxPackagedFile'
    $copied = 0

    foreach ($node in $nodes) {
        $src = $node.Node.Include
        $rel = $node.Node.PackagePath
        if (-not $src -or -not $rel -or -not (Test-Path -LiteralPath $src)) { continue }

        $dst = Join-Path $layoutRoot $rel
        $stale = (-not (Test-Path -LiteralPath $dst)) -or
                 ((Get-Item -LiteralPath $src).LastWriteTimeUtc -gt (Get-Item -LiteralPath $dst).LastWriteTimeUtc)
        if (-not $stale) { continue }

        New-Item -ItemType Directory -Force -Path (Split-Path $dst -Parent) | Out-Null
        Copy-Item -LiteralPath $src -Destination $dst -Force
        $copied++
    }

    if ($copied) { Write-Step "Layout sincronizado pelo appxrecipe ($copied arquivo(s))" }
}

function Get-AppxLayout {
    # Caminho do AppxManifest.xml do layout solto do build atual, gerando-o se preciso.
    $layout = Find-AppxLayout

    if (-not $layout) {
        # Dependendo da versao dos targets do WindowsAppSDK, um 'dotnet build' simples
        # pode nao materializar a pasta AppX\. Nesse caso forcamos a geracao do layout
        # (ainda sem assinar nada -- so precisamos dos arquivos soltos + manifesto).
        Write-Step 'Layout AppX ausente; forcando a geracao'
        & dotnet build $Project `
            -c $Configuration `
            -p:Platform=$Platform `
            -p:RuntimeIdentifier=$Rid `
            -p:GenerateAppxPackageOnBuild=true `
            -p:UapAppxPackageBuildMode=SideloadOnly `
            -p:AppxBundle=Never `
            -p:AppxPackageSigningEnabled=false `
            -p:AppxSymbolPackageEnabled=false
        if ($LASTEXITCODE -ne 0) { throw "Geracao do layout AppX falhou (exit $LASTEXITCODE)." }
        $layout = Find-AppxLayout
    }

    if (-not $layout) {
        throw "AppxManifest.xml nao encontrado sob bin\$Platform\$Configuration. O layout AppX nao foi gerado."
    }

    Sync-AppxLayout $layout
    return $layout
}

function Register-Layout($manifestPath) {
    $layoutRoot = Split-Path $manifestPath -Parent

    # Registrar a mesma versao a partir de outra pasta e ignorado em silencio: o pacote
    # continuaria rodando da pasta antiga (por exemplo, o 'install' em vez do bin\Debug
    # que o 'dev' acabou de compilar). Para trocar de pasta e preciso desregistrar antes.
    $pkg = Get-InstalledPackage
    if ($pkg -and -not [string]::Equals($pkg.InstallLocation.TrimEnd('\'), $layoutRoot.TrimEnd('\'), [StringComparison]::OrdinalIgnoreCase)) {
        Write-Step "Trocando o registro de $($pkg.InstallLocation) para $layoutRoot"
        Invoke-Unregister -PreserveData
    }

    Write-Step "Registrando layout solto: $manifestPath"
    # -Register instala a partir dos arquivos soltos: sem empacotar MSIX e sem
    # assinatura. Exige Developer Mode ligado (Configuracoes > Sistema > Para
    # desenvolvedores). E o mesmo caminho que o F5 do Visual Studio usa por baixo.
    try
    {
        Add-AppxPackage -Register $manifestPath -ForceUpdateFromAnyVersion -ForceApplicationShutdown -ErrorAction Stop
    }
    catch
    {
        # 0x80073CFB: mesma versao ja registrada e o registro novo nao e aceito como
        # atualizacao. Acontece sempre que o Package.appxmanifest muda de forma
        # estrutural (extensoes, capabilities) sem subir a versao. Desregistrar e
        # registrar de novo resolve; os dados do app (login, fila) sao preservados.
        Write-Warning "Registro recusado ($($_.Exception.Message.Split([Environment]::NewLine)[0])). Desregistrando e tentando de novo."
        Invoke-Unregister -PreserveData
        Add-AppxPackage -Register $manifestPath -ForceUpdateFromAnyVersion -ForceApplicationShutdown
    }
}

function Invoke-Register {
    Register-Layout (Get-AppxLayout)
}

function Invoke-Install {
    # Build Release copiado para uma pasta propria e registrado de la, para que o app
    # instalado nao dependa de bin\ (que o 'clean' apaga e todo build reescreve).
    Invoke-Build
    $layoutRoot = Split-Path (Get-AppxLayout) -Parent

    Stop-RunningApp $InstallDir
    Write-Step "Copiando para $InstallDir"
    # /XD Logs: os logs do app instalado ficam na pasta dele e sobrevivem a atualizacao.
    & robocopy $layoutRoot $InstallDir /MIR /XD Logs /R:1 /W:1 /NFL /NDL /NJH /NJS /NP | Out-Null
    # robocopy: 0-7 sao sucesso (1 = copiou, 2 = removeu sobras, ...); 8+ e falha.
    if ($LASTEXITCODE -ge 8) { throw "robocopy falhou (exit $LASTEXITCODE)." }
    $global:LASTEXITCODE = 0

    Register-Layout (Join-Path $InstallDir 'AppxManifest.xml')
    Write-Host "Instalado em $InstallDir" -ForegroundColor Green
}

function Invoke-Launch {
    $pkg = Get-InstalledPackage
    if (-not $pkg) { throw "Pacote nao registrado. Rode '.\build.ps1 dev' primeiro." }
    $appId  = (Get-Manifest).Package.Applications.Application.Id
    $target = "shell:AppsFolder\$($pkg.PackageFamilyName)!$appId"
    Write-Step "Iniciando $target"
    & explorer.exe $target
}

function Resolve-SigningCert {
    # Retorna as propriedades MSBuild que selecionam o certificado de assinatura.
    #
    # Certificado real (TID3_CERT_PFX): assina pelo arquivo .pfx, igual a CI faz.
    # Certificado de dev: assina pelo THUMBPRINT do certificado no store do usuario.
    # Assinar um dev cert pelo .pfx funciona, mas os targets de MSIX emitem
    # APPX0105/APPX0107 ao tentar importar um .pfx protegido por senha -- avisos
    # alarmantes e inuteis, ja que a assinatura acontece mesmo assim.
    if ($env:TID3_CERT_PFX -and (Test-Path $env:TID3_CERT_PFX)) {
        Write-Step "Assinando com certificado de TID3_CERT_PFX"
        return @(
            "-p:PackageCertificateKeyFile=$($env:TID3_CERT_PFX)"
            "-p:PackageCertificatePassword=$($env:TID3_CERT_PASSWORD)"
        )
    }

    # O Subject PRECISA bater exatamente com o Publisher do Package.appxmanifest,
    # senao o empacotamento falha.
    $publisher = (Get-Manifest).Package.Identity.Publisher
    $buildDir  = Join-Path $Root 'build'
    $pfx       = Join-Path $buildDir 'Tid3_Dev.pfx'
    $password  = 'tid3build'

    $cert = Get-ChildItem Cert:\CurrentUser\My |
            Where-Object { $_.Subject -eq $publisher -and $_.HasPrivateKey -and $_.NotAfter -gt (Get-Date) } |
            Sort-Object NotAfter -Descending |
            Select-Object -First 1

    if (-not $cert -and (Test-Path $pfx)) {
        # .pfx existe mas o certificado saiu do store (outra maquina, perfil limpo).
        Write-Step "Importando $pfx para Cert:\CurrentUser\My"
        $cert = Import-PfxCertificate -FilePath $pfx -CertStoreLocation 'Cert:\CurrentUser\My' `
            -Password (ConvertTo-SecureString -String $password -Force -AsPlainText)
    }

    if (-not $cert) {
        Write-Step "Gerando certificado self-signed de dev para $publisher"
        New-Item -ItemType Directory -Force -Path $buildDir | Out-Null
        $cert = New-SelfSignedCertificate `
            -Type Custom `
            -Subject $publisher `
            -KeyUsage DigitalSignature `
            -FriendlyName 'Tid3 Dev' `
            -CertStoreLocation 'Cert:\CurrentUser\My' `
            -TextExtension @('2.5.29.37={text}1.3.6.1.5.5.7.3.3', '2.5.29.19={text}')
        # Exporta o .pfx para dar portabilidade (outra maquina, CI) mesmo que o
        # build local assine pelo thumbprint.
        Export-PfxCertificate -Cert $cert -FilePath $pfx `
            -Password (ConvertTo-SecureString -String $password -Force -AsPlainText) | Out-Null
        Write-Host "    -> $pfx (senha: $password)" -ForegroundColor DarkGray
    }

    Write-Step "Assinando com cert de dev $($cert.Thumbprint)"
    return @(
        "-p:PackageCertificateThumbprint=$($cert.Thumbprint)"
        '-p:AppxPackageSigningEnabled=true'
    )
}

function Invoke-Msix {
    Assert-DotnetSdk
    $signArgs = Resolve-SigningCert
    $outDir   = Join-Path $Root "packages\$Platform"
    New-Item -ItemType Directory -Force -Path $outDir | Out-Null

    # Mesmos parametros de .github/workflows/release.yml, para que o artefato
    # local e o da CI sejam produzidos do mesmo jeito.
    Write-Step "Publicando MSIX $Platform em $outDir"
    & dotnet publish $Project `
        -c Release `
        -p:Platform=$Platform `
        -p:RuntimeIdentifier=$Rid `
        -p:AppxPackageDir="$outDir\" `
        -p:UapAppxPackageBuildMode=SideloadOnly `
        -p:AppxBundle=Never `
        -p:GenerateAppxPackageOnBuild=true `
        @signArgs
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish falhou (exit $LASTEXITCODE)." }

    # O .msix do app fica na raiz da pasta de saida; Dependencies\ traz os
    # runtimes do WindowsAppSDK, que nao interessam aqui.
    $msix = Get-ChildItem -Path $outDir -Recurse -Filter '*.msix' -ErrorAction SilentlyContinue |
            Where-Object { $_.DirectoryName -notmatch '\\Dependencies\\' } |
            Sort-Object LastWriteTime -Descending

    if (-not $msix) {
        Write-Warning "Publish terminou mas nenhum .msix foi encontrado em $outDir."
        return
    }

    Write-Host ''
    Write-Host 'MSIX gerado:' -ForegroundColor Green
    foreach ($file in $msix) {
        $sig = Get-AuthenticodeSignature $file.FullName
        Write-Host "  $($file.FullName)"
        if ($sig.SignerCertificate) {
            # Status 'UnknownError' com signer presente = cadeia nao confiavel,
            # que e o esperado para um certificado self-signed.
            Write-Host "    assinado por $($sig.SignerCertificate.Subject) [$($sig.Status)]" -ForegroundColor DarkGray
        } else {
            Write-Warning "    NAO ASSINADO ($($sig.Status)) -- este pacote nao instala."
        }
    }
    Write-Host ''
    Write-Host 'Com certificado self-signed, instalar em outra maquina exige o certificado' -ForegroundColor DarkGray
    Write-Host 'em LocalMachine\TrustedPeople.' -ForegroundColor DarkGray
}

function Invoke-Unregister([switch]$PreserveData) {
    $pkg = Get-InstalledPackage
    if (-not $pkg) {
        Write-Host 'Nenhum pacote Tid3 registrado.' -ForegroundColor DarkGray
        return
    }
    Write-Step "Removendo $($pkg.PackageFullName)"
    # -PreserveApplicationData mantem LocalState/Settings (login, fila, configuracoes) para o
    # proximo registro. So vale para pacotes registrados de pasta solta (modo dev).
    if ($PreserveData -and $pkg.IsDevelopmentMode) {
        Remove-AppxPackage -Package $pkg.PackageFullName -PreserveApplicationData
    } else {
        Remove-AppxPackage -Package $pkg.PackageFullName
    }
}

function Invoke-Clean {
    foreach ($dir in @('bin', 'obj', 'packages')) {
        $path = Join-Path $Root $dir
        if (Test-Path $path) {
            Write-Step "Removendo $dir\"
            Remove-Item -Recurse -Force $path
        }
    }
}

switch ($Task) {
    'dev' {
        Invoke-Build
        Invoke-Register
        if (-not $NoLaunch) { Invoke-Launch }
    }
    'build'      { Invoke-Build }
    'run'        { Invoke-Launch }
    'install'    { Invoke-Install }
    'msix'       { Invoke-Msix }
    'unregister' { Invoke-Unregister }
    'clean'      { Invoke-Clean }
}
