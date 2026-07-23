# Known USB layout

Observed device:

- Vendor ID: `0x345B`
- Product ID: `0x0002`
- Composite USB device

Interfaces:

| Interface | Class | Purpose | Endpoints |
|---|---:|---|---|
| MI_00 | `0x07` | Printer-class interface | `0x01` OUT, `0x81` IN |
| MI_01 | `0xFF` | Vendor-specific communication | `0x02` OUT, `0x82` IN |

The explorer must bind to `MI_01` only.
