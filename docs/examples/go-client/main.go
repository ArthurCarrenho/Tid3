package main

import (
	"encoding/json"
	"fmt"
	"io/ioutil"
	"log"
	"net/url"
	"strconv"
	"strings"
	"time"

	"github.com/charmbracelet/bubbles/progress"
	"github.com/charmbracelet/bubbles/textinput"
	tea "github.com/charmbracelet/bubbletea"
	"github.com/charmbracelet/lipgloss"
	"github.com/gorilla/websocket"
)

// --- Configuration ---

type Config struct {
	WorkerURL  string `json:"worker_url"`
	UserId     string `json:"user_id"`
	DeviceName string `json:"device_name"`
}

func loadConfig() Config {
	file, err := ioutil.ReadFile("config.json")
	if err != nil {
		return Config{WorkerURL: "your-worker.workers.dev", UserId: "0", DeviceName: "Go Client"}
	}
	var cfg Config
	json.Unmarshal(file, &cfg)
	return cfg
}

// --- Models ---

type Track struct {
	Title  string `json:"Title"`
	Artist string `json:"Artist"`
}

type SyncState struct {
	Queue            []Track `json:"Queue"`
	CurrentIndex     int     `json:"CurrentIndex"`
	PositionSeconds  float64 `json:"PositionSeconds"`
	DurationSeconds  float64 `json:"DurationSeconds"`
	Volume           float64 `json:"Volume"`
	IsPlaying        bool    `json:"IsPlaying"`
	ActiveDeviceName string  `json:"ActiveDeviceName"`
}

type DeviceInfo struct {
	DeviceId   string `json:"deviceId"`
	DeviceName string `json:"deviceName"`
}

type WSMessage struct {
	Type    string          `json:"type"`
	State   *SyncState      `json:"state,omitempty"`
	Data    json.RawMessage `json:"data,omitempty"`
	Devices []DeviceInfo    `json:"devices,omitempty"`
	Device  *DeviceInfo     `json:"device,omitempty"`
	LeftId  string          `json:"deviceId,omitempty"`
}

type syncUpdateMsg WSMessage
type tickMsg time.Time

// --- Styles ---

var (
	titleStyle = lipgloss.NewStyle().
			Bold(true).
			Foreground(lipgloss.Color("#00FFFF")).
			MarginBottom(1)

	statusStyle = lipgloss.NewStyle().
			Foreground(lipgloss.Color("#00FF00"))

	boxStyle = lipgloss.NewStyle().
			Border(lipgloss.RoundedBorder()).
			BorderForeground(lipgloss.Color("#00FFFF")).
			Padding(1).
			Width(85) // Expanded width

	grayStyle    = lipgloss.NewStyle().Foreground(lipgloss.Color("#888888"))
	magentaStyle = lipgloss.NewStyle().Foreground(lipgloss.Color("#FF00FF"))
	whiteStyle   = lipgloss.NewStyle().Foreground(lipgloss.Color("#FFFFFF"))
	greenStyle   = lipgloss.NewStyle().Foreground(lipgloss.Color("#00FF00"))
)

type model struct {
	cfg      Config
	conn     *websocket.Conn
	state    *SyncState
	devices  []DeviceInfo
	progress progress.Model
	input    textinput.Model
	status   string
	deviceId string
}

func initialModel() model {
	cfg := loadConfig()
	ti := textinput.New()
	ti.Placeholder = "play, pause, goto 5, vol 50..."
	ti.Focus()
	ti.CharLimit = 156
	ti.Width = 60

	return model{
		cfg:      cfg,
		progress: progress.New(progress.WithDefaultGradient()),
		input:    ti,
		status:   "Connecting...",
		deviceId: fmt.Sprintf("go-client-%d", time.Now().Unix()),
	}
}

func (m model) Init() tea.Cmd {
	return tea.Batch(
		m.connectToWS(),
		textinput.Blink,
		m.tick(),
	)
}

func (m model) tick() tea.Cmd {
	return tea.Every(time.Second, func(t time.Time) tea.Msg {
		return tickMsg(t)
	})
}

func (m model) connectToWS() tea.Cmd {
	return func() tea.Msg {
		u := url.URL{Scheme: "wss", Host: m.cfg.WorkerURL, Path: "/sync", RawQuery: "userId=" + m.cfg.UserId + "&deviceId=" + m.deviceId + "&deviceName=" + url.QueryEscape(m.cfg.DeviceName)}
		c, _, err := websocket.DefaultDialer.Dial(u.String(), nil)
		if err != nil {
			return nil
		}
		return c
	}
}

func (m model) Update(msg tea.Msg) (tea.Model, tea.Cmd) {
	var cmd tea.Cmd

	switch msg := msg.(type) {
	case *websocket.Conn:
		m.conn = msg
		m.status = "Connected"
		// The listener is started in main() via p.Send

	case tickMsg:
		if m.state != nil && m.state.IsPlaying {
			m.state.PositionSeconds += 1
		}
		return m, m.tick()

	case syncUpdateMsg:
		switch msg.Type {
		case "INIT":
			m.state = msg.State
			m.devices = msg.Devices
		case "SYNC_STATE":
			var newState SyncState
			json.Unmarshal(msg.Data, &newState)
			m.state = &newState
		case "DEVICE_JOINED":
			m.devices = append(m.devices, *msg.Device)
		case "DEVICE_LEFT":
			newDevices := []DeviceInfo{}
			for _, d := range m.devices {
				if d.DeviceId != msg.LeftId {
					newDevices = append(newDevices, d)
				}
			}
			m.devices = newDevices
		}

	case tea.KeyMsg:
		switch msg.String() {
		case "ctrl+c", "esc":
			return m, tea.Quit
		case "enter":
			val := m.input.Value()
			m.sendCommand(val)
			m.input.SetValue("")
			return m, nil
		}
	}

	m.input, cmd = m.input.Update(msg)
	return m, cmd
}

func (m model) sendCommand(input string) {
	if m.conn == nil {
		return
	}
	parts := strings.Split(input, " ")
	cmdType := strings.ToUpper(parts[0])
	var cmdVal string

	if cmdType == "GOTO" && len(parts) > 1 {
		cmdVal = parts[1]
	} else if cmdType == "VOL" && len(parts) > 1 {
		v, _ := strconv.ParseFloat(parts[1], 64)
		cmdVal = fmt.Sprintf("%f", v/100.0)
		cmdType = "VOLUME"
	}

	payload := map[string]interface{}{
		"type": "COMMAND",
		"data": map[string]interface{}{
			"Type":           cmdType,
			"Value":          cmdVal,
			"TargetDeviceId": "",
		},
	}
	b, _ := json.Marshal(payload)
	m.conn.WriteMessage(websocket.TextMessage, b)
}

func (m model) View() string {
	s := titleStyle.Render("? TIDAL SYNC (GO + BUBBLETEA)") + "\n"
	s += grayStyle.Render(fmt.Sprintf("User: %s | Client: %s | Status: ", m.cfg.UserId, m.cfg.DeviceName))
	s += statusStyle.Render(m.status) + "\n"
	s += "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━\n\n"

	if m.state != nil {
		playSymbol := "❚❚ PAUSED"
		if m.state.IsPlaying {
			playSymbol = "▶ PLAYING"
		}

		track := "No Track"
		artist := "Unknown"
		if len(m.state.Queue) > m.state.CurrentIndex && m.state.CurrentIndex >= 0 {
			track = m.state.Queue[m.state.CurrentIndex].Title
			artist = m.state.Queue[m.state.CurrentIndex].Artist
		}

		s += fmt.Sprintf("%s  %s\n", playSymbol, whiteStyle.Bold(true).Render(track))
		s += fmt.Sprintf("Artist:    %s\n", grayStyle.Render(artist))
		s += fmt.Sprintf("Driver:    %s\n", magentaStyle.Render(m.state.ActiveDeviceName))

		dur := m.state.DurationSeconds
		if dur == 0 {
			dur = 1
		}
		pct := m.state.PositionSeconds / dur
		s += fmt.Sprintf("\nProgress:  %s  %s / %s\n",
			m.progress.ViewAs(pct),
			formatTime(m.state.PositionSeconds),
			formatTime(m.state.DurationSeconds))

		s += fmt.Sprintf("Volume:    %d%%\n", int(m.state.Volume*100))

		// --- QUEUE PREVIEW ---
		s += "\n" + lipgloss.NewStyle().Bold(true).Foreground(lipgloss.Color("#00FFFF")).Render("QUEUE PREVIEW:") + "\n"
		start := m.state.CurrentIndex
		if start < 0 {
			start = 0
		}
		end := start + 6
		if end > len(m.state.Queue) {
			end = len(m.state.Queue)
		}

		for i := start; i < end; i++ {
			t := m.state.Queue[i]
			prefix := "   "
			style := grayStyle
			if i == m.state.CurrentIndex {
				prefix = greenStyle.Render(" → ")
				style = whiteStyle.Bold(true)
			}
			s += fmt.Sprintf("[%d] %s%s - %s\n", i, prefix, style.Render(t.Title), grayStyle.Render(t.Artist))
		}
		if len(m.state.Queue) > end {
			s += grayStyle.Render(fmt.Sprintf("   ... and %d more", len(m.state.Queue)-end)) + "\n"
		}

	} else {
		s += "\nWaiting for sync state...\n\n"
	}

	s += "\n" + lipgloss.NewStyle().Bold(true).Foreground(lipgloss.Color("#00FFFF")).Render("OTHER DEVICES:") + "\n"
	activeCount := 0
	for _, d := range m.devices {
		if d.DeviceId != m.deviceId {
			s += fmt.Sprintf("  • %s (%s)\n", d.DeviceName, d.DeviceId[:6])
			activeCount++
		}
	}
	if activeCount == 0 {
		s += grayStyle.Render("  (None)") + "\n"
	}

	s += "\n" + m.input.View() + "\n"
	s += grayStyle.Render("(play, pause, next, prev, goto [index], vol 50, exit)")

	return boxStyle.Render(s)
}

func formatTime(secs float64) string {
	m := int(secs) / 60
	s := int(secs) % 60
	return fmt.Sprintf("%d:%02d", m, s)
}

func main() {
	m := initialModel()
	p := tea.NewProgram(m)

	go func() {
		time.Sleep(500 * time.Millisecond)
		u := url.URL{Scheme: "wss", Host: m.cfg.WorkerURL, Path: "/sync", RawQuery: "userId=" + m.cfg.UserId + "&deviceId=" + m.deviceId + "&deviceName=" + url.QueryEscape(m.cfg.DeviceName)}
		c, _, err := websocket.DefaultDialer.Dial(u.String(), nil)
		if err != nil {
			return
		}
		p.Send(c)

		for {
			_, message, err := c.ReadMessage()
			if err != nil {
				break
			}
			var msg WSMessage
			json.Unmarshal(message, &msg)
			p.Send(syncUpdateMsg(msg))
		}
	}()

	if _, err := p.Run(); err != nil {
		log.Fatal(err)
	}
}
