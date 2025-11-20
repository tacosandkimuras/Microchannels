# Testing Guide for Order Management Bug Fix

## Overview
This guide outlines how to test the order management bug fix in the MCNatev1 strategy (Version 1.20-EnhancedOrderCancellation).

## Prerequisites
- NinjaTrader 8 installed and configured
- Access to market data (live or replay)
- Strategy compiled successfully in NinjaTrader

## Test Scenarios

### Test 1: Basic Order Cancellation on New Microchannel
**Objective:** Verify that when a new microchannel is created, the old pullback order is cancelled.

**Setup:**
- Entry1Level = PB33
- Entry1Qty = 1
- All other entries = NA
- EnableLogging = true
- AllowMultipleMicroChannels = false

**Steps:**
1. Start strategy on a chart with active market conditions
2. Wait for first microchannel to form and PB33 order to be placed
3. Note the order name (e.g., "Short_PB33_1")
4. Wait for a new microchannel to form in the same direction
5. Check logs for cancellation message

**Expected Results:**
```
[Time] >> CancelActivePullbackOrder(SHORT): Order=Short_PB33_1, State=Working
[Time] >> ✗✗✗ CANCELLED: Short_PB33_1 (State was: Working) ✗✗✗
[Time] >> Clearing _activeShortPullbackOrder (cancelled: Short_PB33_1)
[Time] *** ORDER CANCELLED *** Short_PB33_1
```

### Test 2: Order Fills Immediately (Same Bar)
**Objective:** Verify that if an order fills immediately, tracking is properly cleared.

**Setup:**
- Same as Test 1
- Use market replay with volatile conditions

**Steps:**
1. Place strategy on chart
2. Find a microchannel where price immediately pulls back to PB33 level
3. Observe order fill on same bar or next bar
4. Check that tracking is cleared

**Expected Results:**
```
[Time] Short_PB33_X: PB33 @ YYYY.YY Qty=1 [STORED ORDER REFERENCE]
[Time] >> Clearing _activeShortPullbackOrder (filled: Short_PB33_X)
[Time] FILLED: Short_PB33_X @ YYYY.YY | Risk=ZZ.ZZ | Target=WWWW.WW (1R) | Stop=VVVV.VV
```

### Test 3: Multiple Microchannels Mode
**Objective:** Test with AllowMultipleMicroChannels = true

**Setup:**
- AllowMultipleMicroChannels = true
- Entry1Level = PB33
- Entry1Qty = 1

**Steps:**
1. Allow multiple microchannels to form
2. Verify each new microchannel cancels its direction's previous pullback order
3. Check that long and short orders are tracked independently

**Expected Results:**
- Only one long pullback order active at a time
- Only one short pullback order active at a time
- Old orders cancelled when new microchannels form in same direction

### Test 4: Order Rejection Handling
**Objective:** Verify proper cleanup when orders are rejected.

**Setup:**
- Configure strategy with conditions that might cause rejections
- EnableLogging = true

**Expected Results:**
```
[Time] REJECTED: Short_PB33_X - [Error] - [Native Error]
[Time] >> Clearing _activeShortPullbackOrder (rejected: Short_PB33_X)
```

### Test 5: Position Goes Flat
**Objective:** Verify all tracking is cleared when position becomes flat.

**Steps:**
1. Enter a position with pending pullback order
2. Exit via stop loss or target
3. Check logs for cleanup

**Expected Results:**
```
[Time] === POSITION NOW FLAT === Avg=XXXX.XX
[Time] >> Active PB Orders BEFORE clear: Long=NULL, Short=Short_PB33_X
```
Followed by all setups and orders being cleared.

## Log Analysis

### Key Log Messages to Look For

**Successful Cancellation:**
```
>> ✗✗✗ CANCELLED: [OrderName] (State was: [OrderState]) ✗✗✗
```

**Fallback Cancellation (if Order reference was lost):**
```
>> ✗✗✗ CANCELLED BY SIGNAL: [OrderName] (State was: [OrderState]) ✗✗✗
```

**Order Tracking Setup:**
```
[OrderName]: PB33 @ [Price] Qty=[Qty] [STORED ORDER REFERENCE]
```

**Order Tracking Cleared:**
```
>> Clearing _active[Long/Short]PullbackOrder ([filled/cancelled/rejected]: [OrderName])
```

## Common Issues to Watch For

### Issue: Order fills before it can be cancelled
**Symptom:** Old order fills even though a new microchannel was created
**Expected:** This should be RARE now with enhanced cancellation
**Investigation:** Check order state when cancellation was attempted

### Issue: Order reference becomes null
**Symptom:** Log shows "No order to cancel (null)" but order is still working
**Expected:** Fallback mechanism should find and cancel by signal name
**Investigation:** Check for "CANCELLED BY SIGNAL" messages

### Issue: Order not in cancellable state
**Symptom:** Log shows "Cannot cancel [OrderName] - State=[State] (not cancellable)"
**Expected:** Should only occur for Filled/Cancelled/Rejected states
**Investigation:** Verify order state timing

## Performance Metrics

Track these metrics across a full trading session:

1. **Total Microchannels Created:** _____
2. **Total Pullback Orders Placed:** _____
3. **Orders Successfully Cancelled:** _____
4. **Orders That Filled Before Cancellation:** _____
5. **Orders Rejected:** _____
6. **Fallback Cancellations (by signal name):** _____

**Success Rate = (Orders Cancelled / (Orders Cancelled + Orders Filled Before Cancellation)) × 100%**

Target: >95% success rate

## Rollback Plan

If critical issues are found:
1. Revert to Version 1.19 (MCNatev1_Version28.cs from previous commit)
2. Document the specific failure scenario
3. Review logs to identify the gap in the fix

## Notes
- All logs are written to Desktop with filename pattern: `MC-Natev1_YYYYMMDD_HHmmss.txt`
- Check logs in real-time using a text editor with auto-refresh
- Compare behavior with Version 1.19 logs to validate improvements
