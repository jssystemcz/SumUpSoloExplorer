# SumUp Solo Explorer

Experimental Windows diagnostic tool for the SumUp Solo payment terminal.

## Current milestone

The first build is intentionally conservative. It can:

- find `USB\VID_345B&PID_0002&MI_01`;
- open the interface through WinUSB;
- enumerate its USB pipes;
- confirm Bulk OUT `0x02` and Bulk IN `0x82`;
- optionally send an experimental, non-payment `GetDeviceInfo` frame;
- display raw TX/RX bytes.

> This is an independent reverse-engineering project and is not affiliated with or endorsed by SumUp.

## Downloading the EXE from GitHub

1. Open the repository's **Actions** tab.
2. Open the newest successful **Build Windows EXE** run.
3. At the bottom, download the `SumUpSoloExplorer-win-x64` artifact.
4. Extract it and run `SumUpSoloExplorer.exe`.

The build is self-contained, so Visual Studio and .NET do not need to be installed on the test PC.

## Driver requirement

Use Zadig to assign **WinUSB only to**:

```text
USB\VID_345B&PID_0002&MI_01
```

Do not replace the driver for `MI_00`, USB Printing Support, or the composite parent.

## Safety

The **Connect** button only opens the USB interface and reads descriptors. It does not send a command.

The **Get Device Info** button sends an experimental frame derived from static analysis of the Android SDK. It is intended as a non-payment diagnostic request, but the protocol is still under investigation.
