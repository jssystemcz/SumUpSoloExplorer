using System.Diagnostics;
using System.Text;

namespace SumUpSoloExplorer;

internal sealed class MainForm : Form
{
    private readonly Label _status = new()
    {
        AutoSize = true,
        Text = "Odpojeno",
        Font = new Font("Segoe UI", 11, FontStyle.Bold)
    };

    private readonly FlowLayoutPanel _commandButtons = new()
    {
        AutoSize = true,
        WrapContents = true,
        FlowDirection = FlowDirection.LeftToRight
    };

    private readonly TextBox _decoded = new()
    {
        Multiline = true,
        ReadOnly = true,
        ScrollBars = ScrollBars.Both,
        WordWrap = false,
        Font = new Font("Consolas", 10),
        Dock = DockStyle.Fill
    };

    private readonly TextBox _log = new()
    {
        Multiline = true,
        ReadOnly = true,
        ScrollBars = ScrollBars.Both,
        WordWrap = false,
        Font = new Font("Consolas", 10),
        Dock = DockStyle.Fill
    };

    private readonly Button _connect = new() { Text = "Připojit", AutoSize = true };
    private readonly Button _disconnect = new() { Text = "Odpojit", AutoSize = true, Enabled = false };
    private readonly Button _reloadConfig = new() { Text = "Znovu načíst konfiguraci", AutoSize = true };
    private readonly Button _openConfig = new() { Text = "Otevřít config", AutoSize = true };
    private readonly Button _openCaptures = new() { Text = "Otevřít captures", AutoSize = true };
    private readonly Button _clear = new() { Text = "Vymazat log", AutoSize = true };

    private WinUsbDevice? _device;
    private ExplorerConfig _config = new();
    private bool _commandRunning;

    public MainForm()
    {
        Text = "SumUp Solo Explorer – Config Engine";
        Width = 1050;
        Height = 720;
        MinimumSize = new Size(760, 520);
        StartPosition = FormStartPosition.CenterScreen;

        var systemButtons = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            Padding = new Padding(8),
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true
        };
        systemButtons.Controls.AddRange(
            [_connect, _disconnect, _reloadConfig, _openConfig, _openCaptures, _clear]);

        var commandsPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            Padding = new Padding(8, 0, 8, 8),
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true
        };
        commandsPanel.Controls.Add(new Label
        {
            Text = "Příkazy z JSON:",
            AutoSize = true,
            Padding = new Padding(0, 7, 5, 0)
        });
        commandsPanel.Controls.Add(_commandButtons);

        var statusPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            Padding = new Padding(10, 2, 10, 8)
        };
        statusPanel.Controls.Add(new Label
        {
            Text = "Stav:",
            AutoSize = true,
            Padding = new Padding(0, 3, 4, 0)
        });
        statusPanel.Controls.Add(_status);

        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Horizontal,
            SplitterDistance = 320
        };

        var decodedGroup = new GroupBox
        {
            Text = "Dekódovaná odpověď",
            Dock = DockStyle.Fill,
            Padding = new Padding(8)
        };
        decodedGroup.Controls.Add(_decoded);

        var logGroup = new GroupBox
        {
            Text = "Komunikační log",
            Dock = DockStyle.Fill,
            Padding = new Padding(8)
        };
        logGroup.Controls.Add(_log);

        split.Panel1.Controls.Add(decodedGroup);
        split.Panel2.Controls.Add(logGroup);

        Controls.Add(split);
        Controls.Add(statusPanel);
        Controls.Add(commandsPanel);
        Controls.Add(systemButtons);

        _connect.Click += (_, _) => Connect();
        _disconnect.Click += (_, _) => Disconnect();
        _reloadConfig.Click += (_, _) => ReloadConfiguration();
        _openConfig.Click += (_, _) => OpenDirectory(ConfigLoader.ConfigDirectory);
        _openCaptures.Click += (_, _) =>
            OpenDirectory(Path.Combine(AppContext.BaseDirectory, "captures"));
        _clear.Click += (_, _) => _log.Clear();
        FormClosed += (_, _) => Disconnect();

        ReloadConfiguration();
    }

    private void ReloadConfiguration()
    {
        try
        {
            _config = ConfigLoader.Load();
            RebuildCommandButtons();
            Append($"Konfigurace načtena: {_config.Commands.Count} příkazů, " +
                   $"{_config.Tags.Count} tagů, {_config.StatusCodes.Count} stavů.");
        }
        catch (Exception ex)
        {
            Append("CHYBA CONFIGU: " + ex.Message);
            MessageBox.Show(
                this,
                ex.Message,
                "Konfiguraci nelze načíst",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    private void RebuildCommandButtons()
    {
        _commandButtons.SuspendLayout();
        _commandButtons.Controls.Clear();

        foreach (CommandDefinition command in _config.Commands.Where(c => c.Enabled))
        {
            var button = new Button
            {
                Text = command.Name,
                AutoSize = true,
                Enabled = _device is not null && !_commandRunning,
                Tag = command
            };
            button.Click += async (_, _) => await ExecuteCommandAsync(command);
            _commandButtons.Controls.Add(button);
        }

        _commandButtons.ResumeLayout();
    }

    private void Connect()
    {
        Disconnect();
        Append(@"Hledám USB\VID_345B&PID_0002&MI_01 ...");

        try
        {
            _device = WinUsbDevice.OpenSolo();
            _status.Text = "Připojeno";
            _status.ForeColor = Color.DarkGreen;
            _connect.Enabled = false;
            _disconnect.Enabled = true;
            SetCommandButtonsEnabled(true);

            var sb = new StringBuilder();
            sb.AppendLine("Zařízení bylo otevřeno přes WinUSB.");
            sb.AppendLine($"Cesta: {_device.DevicePath}");
            sb.AppendLine($"Interface: {_device.InterfaceNumber}");
            sb.AppendLine("Endpointy:");
            foreach (var pipe in _device.Pipes)
                sb.AppendLine($"  0x{pipe.PipeId:X2}  {pipe.PipeType}  MaxPacket={pipe.MaximumPacketSize}");
            sb.AppendLine();
            sb.AppendLine($"Bulk OUT: {FormatPipe(_device.BulkOutPipe)}");
            sb.AppendLine($"Bulk IN : {FormatPipe(_device.BulkInPipe)}");
            _decoded.Text = sb.ToString();

            Append("OK: WinUSB interface je otevřený.");
            Append($"Bulk OUT: {FormatPipe(_device.BulkOutPipe)}, Bulk IN: {FormatPipe(_device.BulkInPipe)}");
        }
        catch (Exception ex)
        {
            _status.Text = "Chyba";
            _status.ForeColor = Color.DarkRed;
            _decoded.Text = ex.ToString();
            Append("CHYBA: " + ex.Message);
            MessageBox.Show(
                this,
                ex.Message + Environment.NewLine + Environment.NewLine +
                "Zkontroluj, že WinUSB je v Zadigu přiřazen pouze k MI_01.",
                "Připojení selhalo",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    private async Task ExecuteCommandAsync(CommandDefinition command)
    {
        if (_device is null || _commandRunning)
            return;

        _commandRunning = true;
        SetCommandButtonsEnabled(false);

        try
        {
            byte[] frame = command.FrameBytes;
            Append($"COMMAND: {command.Id} – {command.Name}");
            Append($"TX ({frame.Length} B): {HexCodec.Format(frame)}");

            int written = _device.Write(frame);
            Append($"Zapsáno {written} B.");

            // USB čtení musí být co nejrychlejší. Během sběru paketů
            // se nic neparsuje ani nepřekresluje; parsování proběhne až potom.
            List<ReceivedPacket> packets = await Task.Run(
                () => CollectPackets(command));

            foreach (ReceivedPacket packet in packets)
                Append($"RX ({packet.Data.Length} B): {HexCodec.Format(packet.Data)}");

            _decoded.Text = ResponseParser.FormatSession(
                command,
                packets.Select(p => p.Data).ToList(),
                _config);

            string capturePath = CaptureExporter.Save(command, frame, packets);
            Append($"Capture uložen: {capturePath}");

            if (packets.Count == 0)
                Append("RX timeout: nepřišla žádná data.");
        }
        catch (Exception ex)
        {
            Append("CHYBA: " + ex);
            MessageBox.Show(
                this,
                ex.Message,
                "Komunikace selhala",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
        finally
        {
            _commandRunning = false;
            SetCommandButtonsEnabled(_device is not null);
        }
    }

    private List<ReceivedPacket> CollectPackets(CommandDefinition command)
    {
        if (_device is null)
            return [];

        var packets = new List<ReceivedPacket>();
        DateTime deadline = DateTime.UtcNow.AddMilliseconds(command.TimeoutMs);
        DateTime? lastPacketAt = null;

        while (DateTime.UtcNow < deadline)
        {
            try
            {
                byte[]? received = _device.Read(command.ReadTimeoutMs);
                if (received is { Length: > 0 })
                {
                    packets.Add(new ReceivedPacket(DateTimeOffset.Now, received));
                    lastPacketAt = DateTime.UtcNow;
                    continue;
                }
            }
            catch (TimeoutException)
            {
                // Krátký timeout je při polling čtení normální.
            }

            if (lastPacketAt.HasValue &&
                (DateTime.UtcNow - lastPacketAt.Value).TotalMilliseconds
                    >= command.QuietPeriodMs)
            {
                break;
            }
        }

        return packets;
    }

    private void Disconnect()
    {
        _device?.Dispose();
        _device = null;
        _commandRunning = false;
        _status.Text = "Odpojeno";
        _status.ForeColor = SystemColors.ControlText;
        _connect.Enabled = true;
        _disconnect.Enabled = false;
        SetCommandButtonsEnabled(false);
    }

    private void SetCommandButtonsEnabled(bool enabled)
    {
        foreach (Control control in _commandButtons.Controls)
            control.Enabled = enabled;
    }

    private void OpenDirectory(string path)
    {
        Directory.CreateDirectory(path);
        Process.Start(new ProcessStartInfo
        {
            FileName = path,
            UseShellExecute = true
        });
    }

    private void Append(string text)
    {
        _log.AppendText($"[{DateTime.Now:HH:mm:ss.fff}] {text}{Environment.NewLine}");
    }

    private static string FormatPipe(byte? pipe) =>
        pipe.HasValue ? $"0x{pipe.Value:X2}" : "nenalezen";
}
