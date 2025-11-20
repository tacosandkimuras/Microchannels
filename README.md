# MC-Natev1 NinjaTrader Strategy

## Overview
This repository contains the MC-Natev1 trading strategy for NinjaTrader 8.

## Important: NinjaTrader Version Compatibility

⚠️ **This strategy is designed for NinjaTrader 8 only**

### Known Compilation Issues

If you encounter compilation errors like:
```
'OrderState' does not contain a definition for 'PendingSubmit'
'OrderState' does not contain a definition for 'PendingChange'
```

This means you're trying to compile code that uses NinjaTrader 7 OrderState values that don't exist in NinjaTrader 8.

### OrderState Differences Between NT7 and NT8

| NinjaTrader 7 | NinjaTrader 8 |
|---------------|---------------|
| OrderState.PendingSubmit | OrderState.Submitted |
| OrderState.PendingChange | OrderState.ChangeSubmitted |

### Valid OrderState Values in NinjaTrader 8

The following OrderState enum values are available in NinjaTrader 8:

- **Initialized** - Order information validated locally
- **Submitted** - Order submitted to connectivity provider
- **Accepted** - Confirmation from broker
- **Working** - Active at the exchange
- **Suspended** - Held by broker, ready to submit when triggered
- **ChangeSubmitted** - Order modification submitted
- **CancelPending** - Cancellation submitted but not confirmed
- **Cancelled** - Order confirmed cancelled
- **Rejected** - Order rejected
- **PartFilled** - Partially filled (multi-contract orders)
- **Filled** - Fully executed
- **TriggerPending** - Held locally, ready to submit (MIT orders)
- **Unknown** - State cannot be determined

### Cancellable Order States

Orders can be cancelled when in these states:
- Initialized
- Submitted
- Accepted
- Working
- ChangeSubmitted

Orders CANNOT be cancelled when in these states:
- Filled
- PartFilled
- Cancelled
- Rejected

## Installation

1. Ensure you have NinjaTrader 8 installed
2. Import the strategy file `MCNatev1_Version28.cs` into NinjaTrader 8
3. Compile the strategy in the NinjaScript Editor (Tools → Edit NinjaScript → Strategy)

## Compilation

To compile this strategy in NinjaTrader 8:
1. Open NinjaTrader 8
2. Go to Tools → Edit NinjaScript → Strategy
3. Select the MC-Natev1 strategy
4. Press F5 to compile or click Compile

If you encounter errors, ensure:
- You're using NinjaTrader 8 (not NT7)
- No modifications have been made that use NT7 OrderState values
- All required references are included

## Support

For issues related to OrderState compilation errors:
1. Verify you're using NinjaTrader 8
2. Check that no local modifications use `PendingSubmit` or `PendingChange`
3. Replace any NT7 states with their NT8 equivalents as shown in the table above

## Files

- `MCNatev1_Version28.cs` - Main strategy file (NT8 compatible)
- `MC-Natev1_20251120_083711.txt` - Sample execution log
- `README.md` - This file
