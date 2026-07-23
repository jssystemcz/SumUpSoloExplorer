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

    private readonly TextBox _deviceInfo = new()
    {
        Multiline = true,
        ReadOnly = true,
        ScrollBars = ScrollBars.Vertical,
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
    private readonly Button _deviceInfoButton = new() { Text = "Get Device Info", AutoSize = true, Enabled = false };
    private readonly Button _clear = new() { Text = "Vymazat log", AutoSize = true };

    private WinUsbDevice? _device;

    public MainForm()
    {
        Text = "SumUp Solo Explorer";
        Width = 900;
        Height = 650;
        MinimumSize = new Size(700, 500);
        StartPosition = FormStartPosition.CenterScreen;

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            Padding = new Padding(8),
            FlowDirection = FlowDirection.LeftToRight
        };
        buttons.Controls.AddRange([_connect, _disconnect, _deviceInfoButton, _clear]);

        var statusPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            Padding = new Padding(10, 2, 10, 8)
        };
        statusPanel.Controls.Add(new Label { Text = "Stav:", AutoSize = true, Padding = new Padding(0, 3, 4, 0) });
        statusPanel.Controls.Add(_status);

        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Horizontal,
            SplitterDistance = 180
        };

        var infoGroup = new GroupBox { Text = "USB zařízení", Dock = DockStyle.Fill, Padding = new Padding(8) };
        infoGroup.Controls.Add(_deviceInfo);

        var logGroup = new GroupBox { Text = "Komunikační log", Dock = DockStyle.Fill, Padding = new Padding(8) };
        logGroup.Controls.Add(_log);

        split.Panel1.Controls.Add(infoGroup);
        split.Panel2.Controls.Add(logGroup);

        Controls.Add(split);
        Controls.Add(statusPanel);
        Controls.Add(buttons);

        _connect.Click += (_, _) => Connect();
        _disconnect.Click += (_, _) => Disconnect();
        _deviceInfoButton.Click += async (_, _) => await SendDeviceInfoAsync();
        _clear.Click += (_, _) => _log.Clear();
        FormClosed += (_, _) => Disconnect();
    }

    private void Connect()
    {
        Disconnect();
        Append("Hledám USB\\\\VID_345B&PID_0002&MI_01 ...");

        try
        {
            _device = WinUsbDevice.OpenSolo();
            _status.Text = "Připojeno";
            _status.ForeColor = Color.DarkGreen;
            _connect.Enabled = false;
            _disconnect.Enabled = true;
            _deviceInfoButton.Enabled = _device.BulkOutPipe.HasValue && _device.BulkInPipe.HasValue;

            var sb = new StringBuilder();
            sb.AppendLine("Zařízení bylo otevřeno přes WinUSB.");
            sb.AppendLine($"Cesta: {_device.DevicePath}");
            sb.AppendLine($"Interface: {_device.InterfaceNumber}");
            sb.AppendLine($"Endpointy:");
            foreach (var pipe in _device.Pipes)
            {
                sb.AppendLine($"  0x{pipe.PipeId:X2}  {pipe.PipeType}  MaxPacket={pipe.MaximumPacketSize}");
            }

            sb.AppendLine();
            sb.AppendLine($"Bulk OUT: {FormatPipe(_device.BulkOutPipe)}");
            sb.AppendLine($"Bulk IN : {FormatPipe(_device.BulkInPipe)}");
            _deviceInfo.Text = sb.ToString();

            Append("OK: WinUSB interface je otevřený.");
            Append($"Bulk OUT: {FormatPipe(_device.BulkOutPipe)}, Bulk IN: {FormatPipe(_device.BulkInPipe)}");
        }
        catch (Exception ex)
        {
            _status.Text = "Chyba";
            _status.ForeColor = Color.DarkRed;
            _deviceInfo.Text = ex.ToString();
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

    private async Task SendDeviceInfoAsync()
    {
        if (_device is null)
            return;

        _deviceInfoButton.Enabled = false;
        try
        {
            byte[] frame = ProtocolFrames.ExperimentalGetDeviceInfo;
            Append($"TX ({frame.Length} B): {Hex(frame)}");

            int written = _device.Write(frame);
            Append($"Zapsáno {written} B. Čekám maximálně 15 sekund...");

            DateTime until = DateTime.UtcNow.AddSeconds(15);
            bool gotData = false;

            while (DateTime.UtcNow < until)
            {
                byte[]? received = await Task.Run(() => _device.Read(1000));
                if (received is { Length: > 0 })
                {
                    gotData = true;
                    Append($"RX ({received.Length} B): {Hex(received)}");
                }
            }

            if (!gotData)
                Append("RX timeout: nepřišla žádná data.");
        }
        catch (TimeoutException)
        {
            Append("RX timeout.");
        }
        catch (Exception ex)
        {
            Append("CHYBA: " + ex);
            MessageBox.Show(this, ex.Message, "Komunikace selhala", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _deviceInfoButton.Enabled = _device is not null;
        }
    }

    private void Disconnect()
    {
        _device?.Dispose();
        _device = null;
        _status.Text = "Odpojeno";
        _status.ForeColor = SystemColors.ControlText;
        _connect.Enabled = true;
        _disconnect.Enabled = false;
        _deviceInfoButton.Enabled = false;
    }

    private void Append(string text)
    {
        _log.AppendText($"[{DateTime.Now:HH:mm:ss.fff}] {text}{Environment.NewLine}");
    }

    private static string FormatPipe(byte? pipe) => pipe.HasValue ? $"0x{pipe.Value:X2}" : "nenalezen";
    private static string Hex(byte[] data) => Convert.ToHexString(data).Chunk(2).Select(x => new string(x)).Aggregate((a, b) => a + " " + b);
}
