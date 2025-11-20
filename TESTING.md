# Testing Guide for MC-Natev1

## Pre-Compilation Checks

Before compiling in NinjaTrader 8, verify:

1. **File Integrity**
   - MCNatev1_Version28.cs is present
   - File size is approximately 35-40 KB
   - No syntax errors visible in a code editor

2. **OrderState References**
   - Search for "PendingSubmit" - should only appear in comments
   - Search for "PendingChange" - should only appear in comments
   - All actual code uses NT8 states: Submitted, ChangeSubmitted, Working, Accepted, etc.

## Compilation Test

### Steps to Compile

1. Open NinjaTrader 8
2. Navigate to: Tools → Edit NinjaScript → Strategy
3. Select "MCNatev1" from the list
4. Press F5 or click "Compile"

### Expected Results

✅ **Success**: 
- Compilation completes without errors
- Status shows "Compiled successfully"
- No warnings about OrderState

❌ **Failure Scenarios**:

1. **If you see**: `'OrderState' does not contain a definition for 'PendingSubmit'`
   - **Cause**: Local modifications using NT7 states
   - **Fix**: Replace `OrderState.PendingSubmit` with `OrderState.Submitted`

2. **If you see**: `'OrderState' does not contain a definition for 'PendingChange'`
   - **Cause**: Local modifications using NT7 states
   - **Fix**: Replace `OrderState.PendingChange` with `OrderState.ChangeSubmitted`

3. **If you see other compilation errors**:
   - Check that you're using NinjaTrader 8 (not NT7)
   - Verify all required using statements are present
   - Check for local modifications that may have introduced errors

## Runtime Testing

After successful compilation, test the strategy:

### Test 1: Order Cancellation Logic

**Test Scenario**: Verify orders are cancelled in correct states

1. Run strategy in Playback mode
2. Place a pullback order that enters Working or Accepted state
3. Trigger condition that should cancel the order
4. Check log output (if EnableLogging = true)

**Expected Behavior**:
- Orders in Working/Accepted/Submitted states are cancelled
- Log shows: "✗✗✗ CANCELLED: [order name] (State was: [state])"
- Orders in other states show: "Cannot cancel [order name] - State=[state] (not cancellable)"

### Test 2: Multi-MicroChannel Entry

**Test Scenario**: Verify strategy can place multiple entries

1. Run with AllowMultipleMCs = true
2. Create market conditions for multiple channel entries
3. Verify entries are placed correctly
4. Check that old pending orders are cancelled when new ones are placed

**Expected Behavior**:
- Old pullback orders are cancelled before new ones are placed
- No "order already exists" errors
- Position sizing is correct for multiple entries

### Test 3: Position Exit

**Test Scenario**: Verify proper exit behavior

1. Enter a position (long or short)
2. Allow profit target or stop loss to be hit
3. Check that position closes correctly

**Expected Behavior**:
- Exit orders are filled
- Position becomes Flat
- All tracking variables are reset
- Log shows "POSITION NOW FLAT"

## Log File Analysis

Enable logging and check for:

1. **Order State Tracking**
   - All OrderState values in logs should be valid NT8 states
   - No references to PendingSubmit or PendingChange in runtime output

2. **Cancellation Events**
   - Orders are cancelled only in valid states
   - No errors about cancelling already-filled or rejected orders

3. **Entry/Exit Coordination**
   - Pullback orders are properly tracked
   - Old orders are cancelled before new ones are placed
   - Stop loss and profit targets are correctly managed

## Common Issues and Solutions

| Issue | Cause | Solution |
|-------|-------|----------|
| Won't compile in NT8 | Using NT7 states | Replace PendingSubmit→Submitted, PendingChange→ChangeSubmitted |
| Order won't cancel | Order already filled/cancelled | Check order state before CancelOrder call |
| Multiple identical orders | Old order not cancelled | Ensure CancelActivePullbackOrder is called |
| Position tracking wrong | Order state not handled | Check OnOrderUpdate handles all relevant states |

## Code Review Checklist

Before deploying to live trading:

- [ ] Compiles without errors in NinjaTrader 8
- [ ] All OrderState references use NT8 values
- [ ] Tested in Playback/Simulation mode
- [ ] Logging enabled and reviewed
- [ ] Position sizing is correct
- [ ] Risk management (stops/targets) working
- [ ] No memory leaks (check after extended runtime)
- [ ] Order cancellation logic works correctly
- [ ] Multiple entry logic (if enabled) works correctly

## Version History

**Version 28** (Current)
- Fixed NT8 OrderState compatibility
- Added Submitted state to cancellation logic
- Added comprehensive documentation
- Added inline comments for NT7 vs NT8 differences

## Support Resources

- NinjaTrader 8 Documentation: https://ninjatrader.com/support/helpGuides/nt8/
- OrderState Definitions: https://support.ninjatrader.com/s/article/Order-State-Definitions-NinjaTrader-Desktop
- Strategy Development: https://ninjatrader.com/support/helpGuides/nt8/strategy.htm
