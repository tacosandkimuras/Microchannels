#region Using declarations
using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using NinjaTrader.Cbi;
using NinjaTrader.NinjaScript.Indicators;
using System.Windows.Media;
using NinjaTrader.NinjaScript.DrawingTools;
#endregion

namespace NinjaTrader.NinjaScript.Strategies
{
    /// <summary>
    /// MC-Natev1: Configurable continuation and multi-microchannel support.
    /// </summary>
    public class MCNatev1 : Strategy
    {
        #region Enums and Classes
        public enum EntryLevel
        {
            Close,
            PB33,
            PB50,
            PB66,
            NA
        }

        private sealed class ChannelSegment
        {
            public Guid Id { get; } = Guid.NewGuid();
            public int StartBarIndex { get; }
            public double StartPrice { get; }
            public int Length { get; set; } = 1;
            public ChannelSegment(int startBarIndex, double startPrice)
            {
                StartBarIndex = startBarIndex;
                StartPrice = startPrice;
            }
        }

        private class EntryTracker
        {
            public string SignalName { get; set; }
            public double EntryPrice { get; set; }
            public double StopPrice { get; set; }
            public double TargetPrice { get; set; }
            public int Quantity { get; set; }
            public bool Filled { get; set; }
            public bool TargetHit { get; set; }
            public bool StopPlaced { get; set; }
            public EntryLevel TriggerLevel { get; set; }
            public int EntryNumber { get; set; }
            public Guid MicroChannelId { get; set; }
            public bool Cancelled { get; set; }
        }

        private class MicroChannelSetup
        {
            public Guid Id { get; set; } = Guid.NewGuid();
            public Guid SourceChannelId { get; set; }
            public double BaseEntryPrice { get; set; }
            public double StopPrice { get; set; }
            public double Risk { get; set; }
            public bool IsLong { get; set; }
            public List<EntryTracker> Entries { get; set; } = new List<EntryTracker>();
            public int TotalQuantityFilled { get; set; }
            public bool StopSet { get; set; }
            public int ChannelLengthAtEntry { get; set; }
            public DateTime CreatedTime { get; set; }
            public int CreatedBar { get; set; }
        }
        #endregion

        #region State Variables
        private ChannelSegment _activeBullChannel;
        private ChannelSegment _activeBearChannel;
        private double _comparisonTolerance = 1e-8;
        
        private List<MicroChannelSetup> _activeLongSetups = new List<MicroChannelSetup>();
        private List<MicroChannelSetup> _activeShortSetups = new List<MicroChannelSetup>();
        private int _entryCounter = 0;
        
        private int _hardChopStreak;
        private int _cooldownRemaining;
        private ATR _atr;
        private EMA _ema20;
        private StreamWriter _logWriter;
        
        private BarRelation _prevRelation = BarRelation.None;
        private enum BarRelation { None, Inside, Outside }
        private int _stallRelationStreak;
        
        private MicroChannelSetup _mostRecentLongSetup;
        private MicroChannelSetup _mostRecentShortSetup;
        
        // Track the CURRENT active pullback ORDER OBJECTS (not signal names)
        private Order _activeLongPullbackOrder;
        private Order _activeShortPullbackOrder;
        
        // Track signal names to identify orders even if Order reference is lost
        private string _activeLongPullbackSignal;
        private string _activeShortPullbackSignal;
        #endregion

        #region Parameters
        [NinjaScriptProperty]
        [Display(Name = "Min Channel Length", Order = 1, GroupName = "Parameters")]
        public int MinChannelLength { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "ATR Period", Order = 2, GroupName = "Parameters")]
        public int AtrPeriod { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Min ATR", Order = 3, GroupName = "Parameters")]
        public double MinAtr { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "EMA Period", Order = 4, GroupName = "Parameters")]
        public int EmaPeriod { get; set; }

        [NinjaScriptProperty]
        [Range(0.1, double.MaxValue)]
        [Display(Name = "TP Risk Multiplier", Order = 5, GroupName = "Exit Rules", Description = "Profit target = Entry ± (Risk × Multiplier) for ALL entry levels")]
        public double TpRiskMultiplier { get; set; }

        [NinjaScriptProperty]
        [Range(0.1, 100.0)]
        [Display(Name = "Min Risk (Points)", Order = 6, GroupName = "Exit Rules")]
        public double MinRiskPoints { get; set; }

        [NinjaScriptProperty]
        [Range(1.0, 1000.0)]
        [Display(Name = "Max Risk (Points)", Order = 7, GroupName = "Exit Rules")]
        public double MaxRiskPoints { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Stop Loss At Entry Bar", Order = 8, GroupName = "Exit Rules")]
        public bool StopLossAtEntryBar { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Use Stall Exit", Order = 9, GroupName = "Exit Rules", Description = "Exit on consecutive inside/outside bar pattern (disable to only use stop/target)")]
        public bool UseStallExit { get; set; }

        [NinjaScriptProperty]
        [Range(2, 5)]
        [Display(Name = "Stall Pattern Length", Order = 10, GroupName = "Exit Rules", Description = "Number of consecutive inside/outside bars required")]
        public int StallPatternLength { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Entry 1 Level", Order = 1, GroupName = "Entry Levels")]
        public EntryLevel Entry1Level { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Entry 1 Quantity", Order = 2, GroupName = "Entry Levels")]
        public int Entry1Qty { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Entry 2 Level", Order = 3, GroupName = "Entry Levels")]
        public EntryLevel Entry2Level { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Entry 2 Quantity", Order = 4, GroupName = "Entry Levels")]
        public int Entry2Qty { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Entry 3 Level", Order = 5, GroupName = "Entry Levels")]
        public EntryLevel Entry3Level { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Entry 3 Quantity", Order = 6, GroupName = "Entry Levels")]
        public int Entry3Qty { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Entry 4 Level", Order = 7, GroupName = "Entry Levels")]
        public EntryLevel Entry4Level { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Entry 4 Quantity", Order = 8, GroupName = "Entry Levels")]
        public int Entry4Qty { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Require Directional Entry Bar", Order = 1, GroupName = "Entry Filter", Description = "Require current bar to be bull (for long) or bear (for short)")]
        public bool RequireDirectionalEntryBar { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "MicroChannel Purity", Order = 1, GroupName = "Purity Filter")]
        public bool MicroChannelPurity { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, 1.0)]
        [Display(Name = "Strong Close Fraction", Order = 2, GroupName = "Purity Filter")]
        public double StrongCloseFraction { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Enable Chop Filters", Order = 1, GroupName = "Chop Filter", Description = "Enable soft and hard chop detection filters")]
        public bool EnableChopFilters { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, 1.0)]
        [Display(Name = "Soft Overlap Threshold", Order = 2, GroupName = "Chop Filter")]
        public double SoftOverlapThreshold { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, 1.0)]
        [Display(Name = "Soft Window Overlap", Order = 3, GroupName = "Chop Filter")]
        public double SoftWindowOverlap { get; set; }

        [NinjaScriptProperty]
        [Range(0.5, 1.0)]
        [Display(Name = "Hard Overlap Threshold", Order = 4, GroupName = "Chop Filter")]
        public double HardOverlapThreshold { get; set; }

        [NinjaScriptProperty]
        [Range(2, 10)]
        [Display(Name = "Chop Window Bars", Order = 5, GroupName = "Chop Filter")]
        public int ChopWindowBars { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Enable Continuation Scaling", Order = 1, GroupName = "Scaling Options", Description = "Scale into same microchannel as it extends")]
        public bool EnableContinuationScaling { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Continue Scaling After Pullbacks", Order = 2, GroupName = "Scaling Options", Description = "Allow continuation even after pullback fills")]
        public bool ContinueScalingAfterPullbacks { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Allow Multiple MicroChannels", Order = 3, GroupName = "Scaling Options", Description = "Enter new microchannels even when already in position")]
        public bool AllowMultipleMicroChannels { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Strategy Version", Order = 1, GroupName = "Info")]
        public string StrategyVersion { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Enable Debug Logging", Order = 2, GroupName = "Info")]
        public bool EnableLogging { get; set; }

        private const double ShrinkMultiplier = 0.10;
        private const int ChopCooldownBars = 1;
        #endregion

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = "MC-Natev1: Configurable continuation and multi-MC support.";
                Name = "MC-Natev1";
                Calculate = Calculate.OnBarClose;
                BarsRequiredToTrade = 2;
                IsExitOnSessionCloseStrategy = false;
                EntriesPerDirection = 50;
                IsInstantiatedOnEachOptimizationIteration = true;
                
                StopTargetHandling = StopTargetHandling.PerEntryExecution;
                EntryHandling = EntryHandling.AllEntries;
                
                MinChannelLength = 2;
                AtrPeriod = 4;
                MinAtr = 8.0;
                EmaPeriod = 20;
                TpRiskMultiplier = 2.0;
                MinRiskPoints = 5.0;
                MaxRiskPoints = 500.0;
                StrategyVersion = "MC-Natev1.20-EnhancedOrderCancellation";
                MicroChannelPurity = false;
                StrongCloseFraction = 0.70;
                
                RequireDirectionalEntryBar = true;
                
                EnableChopFilters = true;
                SoftOverlapThreshold = 0.7;
                SoftWindowOverlap = 0.7;
                HardOverlapThreshold = 0.9;
                ChopWindowBars = 3;
                
                UseStallExit = false;
                StallPatternLength = 3;
                
                EnableLogging = true;
                StopLossAtEntryBar = true;

                EnableContinuationScaling = false;
                ContinueScalingAfterPullbacks = false;
                AllowMultipleMicroChannels = false;

                Entry1Level = EntryLevel.Close;
                Entry1Qty = 1;
                Entry2Level = EntryLevel.PB33;
                Entry2Qty = 1;
                Entry3Level = EntryLevel.PB50;
                Entry3Qty = 2;
                Entry4Level = EntryLevel.PB66;
                Entry4Qty = 4;
            }
            else if (State == State.Configure)
            {
                RealtimeErrorHandling = RealtimeErrorHandling.StopCancelCloseIgnoreRejects;
            }
            else if (State == State.DataLoaded)
            {
                if (Instrument != null && Instrument.MasterInstrument != null)
                {
                    double tickSize = Instrument.MasterInstrument.TickSize;
                    _comparisonTolerance = tickSize > 0 ? tickSize * 0.01 : 1e-8;
                }
                _atr = ATR(AtrPeriod);
                _ema20 = EMA(EmaPeriod);
                if (EnableLogging)
                {
                    string logPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), $"MC-Natev1_{DateTime.Now:yyyyMMdd_HHmmss}.txt");
                    _logWriter = new StreamWriter(logPath, false) { AutoFlush = true };
                    _logWriter.WriteLine("=== MC-Natev1: ORDER OBJECT TRACKING ===");
                    _logWriter.WriteLine("Version=" + StrategyVersion);
                    _logWriter.WriteLine($"TP Risk Multiplier: {TpRiskMultiplier}");
                    _logWriter.WriteLine($"Require Directional Bar: {RequireDirectionalEntryBar}");
                    _logWriter.WriteLine($"Chop Filters: {EnableChopFilters}");
                    _logWriter.WriteLine($"Stall Exit: {UseStallExit} (Length={StallPatternLength})");
                    _logWriter.WriteLine($"Continuation Scaling: {EnableContinuationScaling}");
                    _logWriter.WriteLine($"Continue After Pullbacks: {ContinueScalingAfterPullbacks}");
                    _logWriter.WriteLine($"Allow Multiple MCs: {AllowMultipleMicroChannels}");
                    _logWriter.WriteLine($"Entry Config: E1={Entry1Level}({Entry1Qty}), E2={Entry2Level}({Entry2Qty}), E3={Entry3Level}({Entry3Qty}), E4={Entry4Level}({Entry4Qty})");
                    _logWriter.WriteLine("STRATEGY: Track Order objects directly, cancel by reference\n");
                }
                ResetState();
            }
            else if (State == State.Terminated)
            {
                if (_logWriter != null)
                {
                    _logWriter.WriteLine("\n=== Strategy Terminated ===");
                    _logWriter.Close();
                    _logWriter = null;
                }
            }
        }

        protected override void OnBarUpdate()
        {
            if (CurrentBar < BarsRequiredToTrade)
                return;

            if (Bars.IsFirstBarOfSession)
            {
                FinalizeBullChannel();
                FinalizeBearChannel();
                _prevRelation = BarRelation.None;
                _stallRelationStreak = 0;
            }

            bool isHardChop = false;
            bool isSoftChop = false;
            
            if (EnableChopFilters && CurrentBar >= ChopWindowBars)
                DetectChop(out isHardChop, out isSoftChop);

            if (!(_cooldownRemaining > 0) && !isSoftChop)
                DetectChannels();

            if (_cooldownRemaining == 0 && !isSoftChop && !isHardChop)
            {
                if (Position.MarketPosition == MarketPosition.Flat)
                {
                    EvaluateLongEntry();
                    EvaluateShortEntry();
                }
                else if (AllowMultipleMicroChannels)
                {
                    EvaluateLongEntry();
                    EvaluateShortEntry();
                }
                else if (EnableContinuationScaling)
                {
                    if (Position.MarketPosition == MarketPosition.Long)
                        EvaluateLongContinuation();
                    else if (Position.MarketPosition == MarketPosition.Short)
                        EvaluateShortContinuation();
                }
            }

            if (UseStallExit && Position.MarketPosition != MarketPosition.Flat && CurrentBar >= 2)
            {
                var relation = DetermineRelation();
                
                if (relation == BarRelation.Inside || relation == BarRelation.Outside)
                {
                    if (_prevRelation == relation)
                    {
                        _stallRelationStreak++;
                    }
                    else
                    {
                        _stallRelationStreak = 1;
                    }
                }
                else
                {
                    _stallRelationStreak = 0;
                }

                if (_stallRelationStreak >= StallPatternLength)
                {
                    if (EnableLogging)
                        _logWriter?.WriteLine($"[{Time[0]}] *** STALL EXIT *** - {_stallRelationStreak} consecutive {relation} bars");

                    if (Position.MarketPosition == MarketPosition.Long)
                        ExitLong();
                    else if (Position.MarketPosition == MarketPosition.Short)
                        ExitShort();

                    _stallRelationStreak = 0;
                }

                _prevRelation = relation;
            }
            else if (Position.MarketPosition == MarketPosition.Flat)
            {
                _stallRelationStreak = 0;
                _prevRelation = BarRelation.None;
            }
        }

        // Cancel the CURRENT active pullback order using the Order object reference
        private void CancelActivePullbackOrder(bool isLong)
        {
            Order activeOrder = isLong ? _activeLongPullbackOrder : _activeShortPullbackOrder;
            string activeSignal = isLong ? _activeLongPullbackSignal : _activeShortPullbackSignal;
            
            if (EnableLogging)
            {
                if (activeOrder == null && activeSignal == null)
                    _logWriter?.WriteLine($"[{Time[0]}] >> CancelActivePullbackOrder({(isLong ? "LONG" : "SHORT")}): No order to cancel (null)");
                else if (activeOrder != null)
                    _logWriter?.WriteLine($"[{Time[0]}] >> CancelActivePullbackOrder({(isLong ? "LONG" : "SHORT")}): Order={activeOrder.Name}, State={activeOrder.OrderState}");
                else
                    _logWriter?.WriteLine($"[{Time[0]}] >> CancelActivePullbackOrder({(isLong ? "LONG" : "SHORT")}): Signal={activeSignal}, searching for order...");
            }
            
            // Try to cancel using Order reference first
            if (activeOrder != null)
            {
                // Cancel if order is in a cancellable state
                if (activeOrder.OrderState == OrderState.Working || 
                    activeOrder.OrderState == OrderState.Accepted ||
                    activeOrder.OrderState == OrderState.PendingSubmit ||
                    activeOrder.OrderState == OrderState.PendingChange)
                {
                    CancelOrder(activeOrder);
                    
                    if (EnableLogging)
                        _logWriter?.WriteLine($"[{Time[0]}] >> ✗✗✗ CANCELLED: {activeOrder.Name} (State was: {activeOrder.OrderState}) ✗✗✗");
                }
                else
                {
                    if (EnableLogging)
                        _logWriter?.WriteLine($"[{Time[0]}] >> Cannot cancel {activeOrder.Name} - State={activeOrder.OrderState} (not cancellable)");
                }
            }
            // Fallback: Try to find and cancel by signal name if Order reference is lost
            else if (activeSignal != null)
            {
                foreach (Order order in Account.Orders)
                {
                    if (order.Name == activeSignal && 
                        (order.OrderState == OrderState.Working || 
                         order.OrderState == OrderState.Accepted ||
                         order.OrderState == OrderState.PendingSubmit ||
                         order.OrderState == OrderState.PendingChange))
                    {
                        CancelOrder(order);
                        
                        if (EnableLogging)
                            _logWriter?.WriteLine($"[{Time[0]}] >> ✗✗✗ CANCELLED BY SIGNAL: {order.Name} (State was: {order.OrderState}) ✗✗✗");
                        break;
                    }
                }
            }

            // Always clear the tracking when this method is called
            if (isLong)
            {
                _activeLongPullbackOrder = null;
                _activeLongPullbackSignal = null;
            }
            else
            {
                _activeShortPullbackOrder = null;
                _activeShortPullbackSignal = null;
            }
        }

        private BarRelation DetermineRelation()
        {
            double curHigh = High[0];
            double curLow = Low[0];
            double prevHigh = High[1];
            double prevLow = Low[1];
            if (curHigh >= prevHigh - _comparisonTolerance && curLow <= prevLow + _comparisonTolerance) return BarRelation.Outside;
            if (curHigh <= prevHigh + _comparisonTolerance && curLow >= prevLow - _comparisonTolerance) return BarRelation.Inside;
            return BarRelation.None;
        }

        #region Chop Detection
        private void DetectChop(out bool isHardChop, out bool isSoftChop)
        {
            isHardChop = false; isSoftChop = false;
            if (_cooldownRemaining > 0)
            {
                if (CheckTrendResumption()) { _cooldownRemaining = 0; _hardChopStreak = 0; } else { _cooldownRemaining--; }
            }
            double seqOverlap = CalculateSequentialOverlap(0, 1);
            double winOverlap = CalculateWindowOverlap(ChopWindowBars);
            bool hardHit = seqOverlap >= HardOverlapThreshold;
            _hardChopStreak = hardHit ? _hardChopStreak + 1 : 0;
            bool hardStreakChop = _hardChopStreak >= 2;
            bool rangeCompression = CheckRangeCompression();
            if (hardStreakChop || rangeCompression)
            {
                isHardChop = true;
                FinalizeBullChannel();
                FinalizeBearChannel();
                _cooldownRemaining = ChopCooldownBars;
                _hardChopStreak = 0;
                
                if (EnableLogging)
                    _logWriter?.WriteLine($"[{Time[0]}] HARD CHOP detected - cooldown active");
                return;
            }
            if (seqOverlap >= SoftOverlapThreshold || winOverlap >= SoftWindowOverlap)
            {
                isSoftChop = true;
            }
        }
        private double CalculateSequentialOverlap(int bar1, int bar2)
        {
            if (CurrentBar - bar1 < 0 || CurrentBar - bar2 < 0) return 0;
            double hi1 = High[bar1]; double lo1 = Low[bar1]; double hi2 = High[bar2]; double lo2 = Low[bar2];
            double overlapHi = Math.Min(hi1, hi2); double overlapLo = Math.Max(lo1, lo2); double overlap = Math.Max(0, overlapHi - overlapLo);
            double range1 = hi1 - lo1; double range2 = hi2 - lo2; double minRange = Math.Min(range1, range2);
            if (minRange <= TickSize) return 0; return overlap / minRange;
        }
        private double CalculateWindowOverlap(int windowSize)
        {
            if (CurrentBar < windowSize - 1) return 0;
            double windowHi = High[0]; double windowLo = Low[0];
            for (int i = 1; i < windowSize; i++) { windowHi = Math.Max(windowHi, High[i]); windowLo = Math.Min(windowLo, Low[i]); }
            double span = windowHi - windowLo; if (span <= TickSize) return 1.0;
            double overlapHi = Math.Min(High[0], windowHi); double overlapLo = Math.Max(Low[0], windowLo); double overlap = Math.Max(0, overlapHi - overlapLo);
            return overlap / span;
        }
        private bool CheckRangeCompression()
        {
            if (CurrentBar < ChopWindowBars || _atr[0] <= 0) return false;
            double avgRange = 0; for (int i = 0; i < ChopWindowBars; i++) avgRange += (High[i] - Low[i]); avgRange /= ChopWindowBars;
            return avgRange < (ShrinkMultiplier * _atr[0]);
        }
        private bool CheckTrendResumption() => IsStrongTrendBar(true) || IsStrongTrendBar(false);
        private bool IsStrongTrendBar(bool bullish)
        {
            double windowHi = High[0]; double windowLo = Low[0];
            for (int i = 1; i <= Math.Min(2, CurrentBar); i++) { windowHi = Math.Max(windowHi, High[i]); windowLo = Math.Min(windowLo, Low[i]); }
            if (bullish)
            {
                if (Close[0] <= Open[0]) return false; if (Close[0] <= windowHi) return false; if (Close[0] <= _ema20[0]) return false;
                if (CurrentBar >= 2) return (Close[0] > Close[1] && Close[1] > Close[2]); return true;
            }
            else
            {
                if (Close[0] >= Open[0]) return false; if (Close[0] >= windowLo) return false; if (Close[0] >= _ema20[0]) return false;
                if (CurrentBar >= 2) return (Close[0] < Close[1] && Close[1] < Close[2]); return true;
            }
        }
        #endregion

        #region Channel Detection
        private bool IsBullBar(int barsAgo) => Close[barsAgo] >= Open[barsAgo];
        private bool IsBearBar(int barsAgo) => Close[barsAgo] <= Open[barsAgo];
        private void DetectChannels()
        {
            if (CurrentBar < 1) return;
            if (Time[0].Date != Time[1].Date)
            {
                FinalizeBullChannel(); FinalizeBearChannel();
                return;
            }
            bool bullCondition = IsGreaterOrEqual(Low[0], Low[1]);
            if (bullCondition)
            {
                if (MicroChannelPurity && !IsBullBar(0)) FinalizeBullChannel(); else ContinueBullChannel(1);
            }
            else FinalizeBullChannel();
            bool bearCondition = IsLessOrEqual(High[0], High[1]);
            if (bearCondition)
            {
                if (MicroChannelPurity && !IsBearBar(0)) FinalizeBearChannel(); else ContinueBearChannel(1);
            }
            else FinalizeBearChannel();
        }
        private void ContinueBullChannel(int prevBarsAgo)
        {
            if (_activeBullChannel == null)
            {
                if (MicroChannelPurity && !IsBullBar(prevBarsAgo)) return;
                _activeBullChannel = new ChannelSegment(CurrentBar - prevBarsAgo, Low[prevBarsAgo]);
            }
            _activeBullChannel.Length = CurrentBar - _activeBullChannel.StartBarIndex + 1;
        }
        private void FinalizeBullChannel() => _activeBullChannel = null;
        private void ContinueBearChannel(int prevBarsAgo)
        {
            if (_activeBearChannel == null)
            {
                if (MicroChannelPurity && !IsBearBar(prevBarsAgo)) return;
                _activeBearChannel = new ChannelSegment(CurrentBar - prevBarsAgo, High[prevBarsAgo]);
            }
            _activeBearChannel.Length = CurrentBar - _activeBearChannel.StartBarIndex + 1;
        }
        private void FinalizeBearChannel() => _activeBearChannel = null;
        private bool IsGreaterOrEqual(double a, double b) => a >= b - _comparisonTolerance;
        private bool IsLessOrEqual(double a, double b) => a <= b + _comparisonTolerance;
        #endregion

        #region Anchor Bar Selection
        private int FindBullAnchorBarsAgo()
        {
            if (_activeBullChannel == null) return 0;
            int startIndex = _activeBullChannel.StartBarIndex;
            int maxLookBackBarsAgo = CurrentBar - startIndex;
            for (int barsAgo = 0; barsAgo <= maxLookBackBarsAgo; barsAgo++)
            {
                bool counter = Close[barsAgo] < Open[barsAgo];
                double range = High[barsAgo] - Low[barsAgo];
                double strongThreshold = Low[barsAgo] + (range * StrongCloseFraction);
                bool weak = Close[barsAgo] < strongThreshold;
                if (!counter && !weak)
                    return barsAgo;
            }
            return maxLookBackBarsAgo;
        }
        private int FindBearAnchorBarsAgo()
        {
            if (_activeBearChannel == null) return 0;
            int startIndex = _activeBearChannel.StartBarIndex;
            int maxLookBackBarsAgo = CurrentBar - startIndex;
            for (int barsAgo = 0; barsAgo <= maxLookBackBarsAgo; barsAgo++)
            {
                bool counter = Close[barsAgo] > Open[barsAgo];
                double range = High[barsAgo] - Low[barsAgo];
                double strongThreshold = High[barsAgo] - (range * StrongCloseFraction);
                bool weak = Close[barsAgo] > strongThreshold;
                if (!counter && !weak)
                    return barsAgo;
            }
            return maxLookBackBarsAgo;
        }
        private bool IsGoodBullExtensionBar()
        {
            bool counter = Close[0] < Open[0];
            double range = High[0] - Low[0];
            double strongThreshold = Low[0] + (range * StrongCloseFraction);
            bool weak = Close[0] < strongThreshold;
            return !counter && !weak;
        }
        private bool IsGoodBearExtensionBar()
        {
            bool counter = Close[0] > Open[0];
            double range = High[0] - Low[0];
            double strongThreshold = High[0] - (range * StrongCloseFraction);
            bool weak = Close[0] > strongThreshold;
            return !counter && !weak;
        }
        #endregion

        #region Entry Logic
        private void EvaluateLongEntry()
        {
            if (EnableLogging)
                _logWriter?.WriteLine($"[{Time[0]}] --- EvaluateLongEntry START ---");

            if (_atr[0] < MinAtr)
            {
                if (EnableLogging)
                    _logWriter?.WriteLine($"[{Time[0]}] REJECT: ATR too low ({_atr[0]:F2} < {MinAtr})");
                return;
            }

            if (Close[0] <= _ema20[0])
            {
                if (EnableLogging)
                    _logWriter?.WriteLine($"[{Time[0]}] REJECT: Close <= EMA20 ({Close[0]:F2} <= {_ema20[0]:F2})");
                return;
            }

            if (RequireDirectionalEntryBar && !IsBullBar(0))
            {
                if (EnableLogging)
                    _logWriter?.WriteLine($"[{Time[0]}] REJECT: Not a bull bar (Close={Close[0]:F2}, Open={Open[0]:F2})");
                return;
            }

            if (_activeBullChannel == null || _activeBullChannel.Length < MinChannelLength)
            {
                if (EnableLogging)
                    _logWriter?.WriteLine($"[{Time[0]}] REJECT: No qualifying bull channel");
                return;
            }

            var existingSetup = _activeLongSetups.FirstOrDefault(s => s.SourceChannelId == _activeBullChannel.Id);
            if (existingSetup != null && !AllowMultipleMicroChannels)
            {
                if (EnableLogging)
                    _logWriter?.WriteLine($"[{Time[0]}] REJECT: Already entered channel {_activeBullChannel.Id}");
                return;
            }

            int anchorBarsAgo = StopLossAtEntryBar ? FindBullAnchorBarsAgo() : 0;
            double baseEntry = Close[anchorBarsAgo];
            double stopPrice = StopLossAtEntryBar ? Low[anchorBarsAgo] - TickSize : _activeBullChannel.StartPrice - TickSize;
            double risk = baseEntry - stopPrice;

            if (risk <= 0 || risk > MaxRiskPoints || risk < MinRiskPoints)
            {
                if (EnableLogging)
                    _logWriter?.WriteLine($"[{Time[0]}] REJECT: Risk={risk:F2} (Min={MinRiskPoints}, Max={MaxRiskPoints})");
                return;
            }

            // CANCEL ANY EXISTING LONG PULLBACK ORDER BEFORE CREATING NEW SETUP
            CancelActivePullbackOrder(true);

            var setup = new MicroChannelSetup
            {
                SourceChannelId = _activeBullChannel.Id,
                BaseEntryPrice = baseEntry,
                StopPrice = stopPrice,
                Risk = risk,
                IsLong = true,
                ChannelLengthAtEntry = _activeBullChannel.Length,
                CreatedTime = Time[0],
                CreatedBar = CurrentBar
            };
            _activeLongSetups.Add(setup);
            _mostRecentLongSetup = setup;

            PlaceConfiguredEntries(setup);

            if (EnableLogging)
                _logWriter?.WriteLine($"[{Time[0]}] ✓ LONG MC @ Bar {CurrentBar}: Base={baseEntry:F2} Stop={stopPrice:F2} Risk={risk:F2}");
        }

        private void EvaluateShortEntry()
        {
            if (EnableLogging)
                _logWriter?.WriteLine($"[{Time[0]}] --- EvaluateShortEntry START ---");

            if (_atr[0] < MinAtr)
            {
                if (EnableLogging)
                    _logWriter?.WriteLine($"[{Time[0]}] REJECT: ATR too low");
                return;
            }

            if (Close[0] >= _ema20[0])
            {
                if (EnableLogging)
                    _logWriter?.WriteLine($"[{Time[0]}] REJECT: Close >= EMA20 ({Close[0]:F2} >= {_ema20[0]:F2})");
                return;
            }

            if (RequireDirectionalEntryBar && !IsBearBar(0))
            {
                if (EnableLogging)
                    _logWriter?.WriteLine($"[{Time[0]}] REJECT: Not a bear bar (Close={Close[0]:F2}, Open={Open[0]:F2})");
                return;
            }

            if (_activeBearChannel == null || _activeBearChannel.Length < MinChannelLength)
            {
                if (EnableLogging)
                    _logWriter?.WriteLine($"[{Time[0]}] REJECT: No qualifying bear channel");
                return;
            }

            var existingSetup = _activeShortSetups.FirstOrDefault(s => s.SourceChannelId == _activeBearChannel.Id);
            if (existingSetup != null && !AllowMultipleMicroChannels)
            {
                if (EnableLogging)
                    _logWriter?.WriteLine($"[{Time[0]}] REJECT: Already entered channel {_activeBearChannel.Id}");
                return;
            }

            int anchorBarsAgo = StopLossAtEntryBar ? FindBearAnchorBarsAgo() : 0;
            double baseEntry = Close[anchorBarsAgo];
            double stopPrice = StopLossAtEntryBar ? High[anchorBarsAgo] + TickSize : _activeBearChannel.StartPrice + TickSize;
            double risk = stopPrice - baseEntry;

            if (risk <= 0 || risk > MaxRiskPoints || risk < MinRiskPoints)
            {
                if (EnableLogging)
                    _logWriter?.WriteLine($"[{Time[0]}] REJECT: Risk={risk:F2}");
                return;
            }

            // CANCEL ANY EXISTING SHORT PULLBACK ORDER BEFORE CREATING NEW SETUP
            CancelActivePullbackOrder(false);

            var setup = new MicroChannelSetup
            {
                SourceChannelId = _activeBearChannel.Id,
                BaseEntryPrice = baseEntry,
                StopPrice = stopPrice,
                Risk = risk,
                IsLong = false,
                ChannelLengthAtEntry = _activeBearChannel.Length,
                CreatedTime = Time[0],
                CreatedBar = CurrentBar
            };
            _activeShortSetups.Add(setup);
            _mostRecentShortSetup = setup;

            PlaceConfiguredEntries(setup);

            if (EnableLogging)
                _logWriter?.WriteLine($"[{Time[0]}] ✓ SHORT MC @ Bar {CurrentBar}: Base={baseEntry:F2} Stop={stopPrice:F2} Risk={risk:F2}");
        }

        private void EvaluateLongContinuation()
        {
            if (_activeBullChannel == null) return;

            var currentSetup = _activeLongSetups.LastOrDefault(s => s.SourceChannelId == _activeBullChannel.Id);
            if (currentSetup == null) return;

            if (_activeBullChannel.Length <= currentSetup.ChannelLengthAtEntry)
                return;

            if (!IsGoodBullExtensionBar())
                return;

            if (RequireDirectionalEntryBar && !IsBullBar(0))
                return;

            if (_atr[0] < MinAtr || Close[0] <= _ema20[0])
                return;

            if (!ContinueScalingAfterPullbacks)
            {
                bool anyPullbackFilled = currentSetup.Entries.Any(e => e.TriggerLevel != EntryLevel.Close && e.Filled);
                if (anyPullbackFilled)
                    return;
            }

            double candidateEntry = Close[0];
            double candidateStop = Low[0] - TickSize;
            double risk = candidateEntry - candidateStop;

            if (risk <= 0 || risk > MaxRiskPoints || risk < MinRiskPoints)
                return;

            // CANCEL ANY EXISTING LONG PULLBACK ORDER
            CancelActivePullbackOrder(true);

            var contSetup = new MicroChannelSetup
            {
                SourceChannelId = _activeBullChannel.Id,
                BaseEntryPrice = candidateEntry,
                StopPrice = candidateStop,
                Risk = risk,
                IsLong = true,
                ChannelLengthAtEntry = _activeBullChannel.Length,
                CreatedTime = Time[0],
                CreatedBar = CurrentBar
            };
            _activeLongSetups.Add(contSetup);
            _mostRecentLongSetup = contSetup;

            PlaceConfiguredEntries(contSetup);

            if (EnableLogging)
                _logWriter?.WriteLine($"[{Time[0]}] ✓ LONG CONT @ Bar {CurrentBar}");
        }

        private void EvaluateShortContinuation()
        {
            if (_activeBearChannel == null) return;

            var currentSetup = _activeShortSetups.LastOrDefault(s => s.SourceChannelId == _activeBearChannel.Id);
            if (currentSetup == null) return;

            if (_activeBearChannel.Length <= currentSetup.ChannelLengthAtEntry)
                return;

            if (!IsGoodBearExtensionBar())
                return;

            if (RequireDirectionalEntryBar && !IsBearBar(0))
                return;

            if (_atr[0] < MinAtr || Close[0] >= _ema20[0])
                return;

            if (!ContinueScalingAfterPullbacks)
            {
                bool anyPullbackFilled = currentSetup.Entries.Any(e => e.TriggerLevel != EntryLevel.Close && e.Filled);
                if (anyPullbackFilled)
                    return;
            }

            double candidateEntry = Close[0];
            double candidateStop = High[0] + TickSize;
            double risk = candidateStop - candidateEntry;

            if (risk <= 0 || risk > MaxRiskPoints || risk < MinRiskPoints)
                return;

            // CANCEL ANY EXISTING SHORT PULLBACK ORDER
            CancelActivePullbackOrder(false);

            var contSetup = new MicroChannelSetup
            {
                SourceChannelId = _activeBearChannel.Id,
                BaseEntryPrice = candidateEntry,
                StopPrice = candidateStop,
                Risk = risk,
                IsLong = false,
                ChannelLengthAtEntry = _activeBearChannel.Length,
                CreatedTime = Time[0],
                CreatedBar = CurrentBar
            };
            _activeShortSetups.Add(contSetup);
            _mostRecentShortSetup = contSetup;

            PlaceConfiguredEntries(contSetup);

            if (EnableLogging)
                _logWriter?.WriteLine($"[{Time[0]}] ✓ SHORT CONT @ Bar {CurrentBar}");
        }

        private void PlaceConfiguredEntries(MicroChannelSetup setup)
        {
            var entryConfigs = new[]
            {
                new { Level = Entry1Level, Qty = Entry1Qty, Number = 1 },
                new { Level = Entry2Level, Qty = Entry2Qty, Number = 2 },
                new { Level = Entry3Level, Qty = Entry3Qty, Number = 3 },
                new { Level = Entry4Level, Qty = Entry4Qty, Number = 4 }
            };

            foreach (var config in entryConfigs)
            {
                if (config.Level == EntryLevel.NA || config.Qty <= 0) continue;

                double entryPrice = GetEntryPrice(setup, config.Level);
                string signalName = $"{(setup.IsLong ? "Long" : "Short")}_{config.Level}_{++_entryCounter}";

                var tracker = new EntryTracker
                {
                    SignalName = signalName,
                    EntryPrice = entryPrice,
                    StopPrice = setup.StopPrice,
                    TargetPrice = setup.IsLong ? 
                        setup.BaseEntryPrice + (TpRiskMultiplier * setup.Risk) : 
                        setup.BaseEntryPrice - (TpRiskMultiplier * setup.Risk),
                    Quantity = config.Qty,
                    TriggerLevel = config.Level,
                    EntryNumber = config.Number,
                    MicroChannelId = setup.Id
                };

                setup.Entries.Add(tracker);

                if (config.Level == EntryLevel.Close)
                {
                    if (setup.IsLong)
                        EnterLong(config.Qty, signalName);
                    else
                        EnterShort(config.Qty, signalName);
                    
                    if (EnableLogging)
                        _logWriter?.WriteLine($"  {signalName}: {config.Level} @ MARKET Qty={config.Qty}");
                }
                else
                {
                    // This is a pullback order - store the Order object reference AND signal name
                    Order placedOrder = null;
                    
                    if (setup.IsLong)
                    {
                        placedOrder = EnterLongLimit(0, true, config.Qty, entryPrice, signalName);
                        _activeLongPullbackOrder = placedOrder;
                        _activeLongPullbackSignal = signalName;
                        
                        if (EnableLogging)
                            _logWriter?.WriteLine($"  {signalName}: {config.Level} @ {entryPrice:F2} Qty={config.Qty} [STORED ORDER REFERENCE]");
                    }
                    else
                    {
                        placedOrder = EnterShortLimit(0, true, config.Qty, entryPrice, signalName);
                        _activeShortPullbackOrder = placedOrder;
                        _activeShortPullbackSignal = signalName;
                        
                        if (EnableLogging)
                            _logWriter?.WriteLine($"  {signalName}: {config.Level} @ {entryPrice:F2} Qty={config.Qty} [STORED ORDER REFERENCE]");
                    }
                }
            }
        }

        private double GetEntryPrice(MicroChannelSetup setup, EntryLevel level)
        {
            switch (level)
            {
                case EntryLevel.Close:
                    return setup.BaseEntryPrice;
                case EntryLevel.PB33:
                    return setup.IsLong ? 
                        setup.BaseEntryPrice - (setup.Risk * 0.33) : 
                        setup.BaseEntryPrice + (setup.Risk * 0.33);
                case EntryLevel.PB50:
                    return setup.IsLong ? 
                        setup.BaseEntryPrice - (setup.Risk * 0.50) : 
                        setup.BaseEntryPrice + (setup.Risk * 0.50);
                case EntryLevel.PB66:
                    return setup.IsLong ? 
                        setup.BaseEntryPrice - (setup.Risk * 0.66) : 
                        setup.BaseEntryPrice + (setup.Risk * 0.66);
                default:
                    return setup.BaseEntryPrice;
            }
        }
        #endregion

        #region Order Execution Tracking
        protected override void OnOrderUpdate(Order order, double limitPrice, double stopPrice, int quantity, int filled, double averageFillPrice, OrderState orderState, DateTime time, ErrorCode error, string nativeError)
        {
            if (orderState == OrderState.Rejected)
            {
                if (EnableLogging)
                    _logWriter?.WriteLine($"[{Time[0]}] REJECTED: {order.Name} - {error} - {nativeError}");
                
                // Clear tracking if this was the active pullback order
                if (_activeLongPullbackSignal == order.Name)
                {
                    _activeLongPullbackOrder = null;
                    _activeLongPullbackSignal = null;
                    if (EnableLogging)
                        _logWriter?.WriteLine($"[{Time[0]}] >> Clearing _activeLongPullbackOrder (rejected: {order.Name})");
                }
                else if (_activeShortPullbackSignal == order.Name)
                {
                    _activeShortPullbackOrder = null;
                    _activeShortPullbackSignal = null;
                    if (EnableLogging)
                        _logWriter?.WriteLine($"[{Time[0]}] >> Clearing _activeShortPullbackOrder (rejected: {order.Name})");
                }
                return;
            }

            if (orderState == OrderState.Filled && !order.Name.Contains("_Target") && !order.Name.Contains("_Stop") && !order.Name.Contains("Profit target") && !order.Name.Contains("Stop loss"))
            {
                MicroChannelSetup setup = null;
                EntryTracker tracker = null;

                foreach (var s in _activeLongSetups)
                {
                    tracker = s.Entries.FirstOrDefault(e => e.SignalName == order.Name);
                    if (tracker != null)
                    {
                        setup = s;
                        break;
                    }
                }

                if (setup == null)
                {
                    foreach (var s in _activeShortSetups)
                    {
                        tracker = s.Entries.FirstOrDefault(e => e.SignalName == order.Name);
                        if (tracker != null)
                        {
                            setup = s;
                            break;
                        }
                    }
                }

                if (setup != null && tracker != null)
                {
                    tracker.Filled = true;
                    setup.TotalQuantityFilled += filled;

                    // Clear the active pullback tracking since it filled
                    if (_activeLongPullbackSignal == order.Name)
                    {
                        if (EnableLogging)
                            _logWriter?.WriteLine($"[{Time[0]}] >> Clearing _activeLongPullbackOrder (filled: {order.Name})");
                        _activeLongPullbackOrder = null;
                        _activeLongPullbackSignal = null;
                    }
                    else if (_activeShortPullbackSignal == order.Name)
                    {
                        if (EnableLogging)
                            _logWriter?.WriteLine($"[{Time[0]}] >> Clearing _activeShortPullbackOrder (filled: {order.Name})");
                        _activeShortPullbackOrder = null;
                        _activeShortPullbackSignal = null;
                    }

                    double riskFromFill = setup.IsLong ? 
                        (averageFillPrice - setup.StopPrice) : 
                        (setup.StopPrice - averageFillPrice);
                    
                    double targetPrice = setup.IsLong ? 
                        averageFillPrice + (riskFromFill * TpRiskMultiplier) : 
                        averageFillPrice - (riskFromFill * TpRiskMultiplier);
                    
                    if (!setup.StopSet)
                    {
                        SetStopLoss(tracker.SignalName, CalculationMode.Price, setup.StopPrice, false);
                        setup.StopSet = true;
                    }
                    
                    double targetDistance = Math.Abs(targetPrice - averageFillPrice);
                    int targetTicks = (int)Math.Round(targetDistance / TickSize);
                    SetProfitTarget(tracker.SignalName, CalculationMode.Ticks, targetTicks);

                    if (EnableLogging)
                        _logWriter?.WriteLine($"[{Time[0]}] FILLED: {tracker.SignalName} @ {averageFillPrice:F2} | Risk={riskFromFill:F2} | Target={targetPrice:F2} ({TpRiskMultiplier}R) | Stop={setup.StopPrice:F2}");
                }
            }
            
            if (orderState == OrderState.Filled && order.Name.Contains("Profit target"))
            {
                if (EnableLogging)
                    _logWriter?.WriteLine($"[{Time[0]}] *** PROFIT TARGET HIT *** {order.Name} @ {averageFillPrice:F2}");
            }
            
            if (orderState == OrderState.Filled && order.Name.Contains("Stop loss"))
            {
                if (EnableLogging)
                    _logWriter?.WriteLine($"[{Time[0]}] *** STOP LOSS HIT *** {order.Name} @ {averageFillPrice:F2}");
            }
            
            if (orderState == OrderState.Cancelled)
            {
                // Clear tracking if this was the active pullback
                if (_activeLongPullbackSignal == order.Name)
                {
                    if (EnableLogging)
                        _logWriter?.WriteLine($"[{Time[0]}] >> Clearing _activeLongPullbackOrder (cancelled: {order.Name})");
                    _activeLongPullbackOrder = null;
                    _activeLongPullbackSignal = null;
                }
                else if (_activeShortPullbackSignal == order.Name)
                {
                    if (EnableLogging)
                        _logWriter?.WriteLine($"[{Time[0]}] >> Clearing _activeShortPullbackOrder (cancelled: {order.Name})");
                    _activeShortPullbackOrder = null;
                    _activeShortPullbackSignal = null;
                }
                    
                if (EnableLogging)
                    _logWriter?.WriteLine($"[{Time[0]}] *** ORDER CANCELLED *** {order.Name}");
            }
        }
        #endregion

        #region Helpers
        private void ResetState()
        {
            _activeBullChannel = null;
            _activeBearChannel = null;
            _hardChopStreak = 0;
            _cooldownRemaining = 0;
            _prevRelation = BarRelation.None;
            _stallRelationStreak = 0;
            _activeLongSetups.Clear();
            _activeShortSetups.Clear();
            _mostRecentLongSetup = null;
            _mostRecentShortSetup = null;
            _entryCounter = 0;
            _activeLongPullbackOrder = null;
            _activeShortPullbackOrder = null;
            _activeLongPullbackSignal = null;
            _activeShortPullbackSignal = null;
        }

        protected override void OnPositionUpdate(Position position, double averagePrice, int quantity, MarketPosition marketPosition)
        {
            if (marketPosition == MarketPosition.Flat && quantity == 0)
            {
                if (EnableLogging)
                {
                    _logWriter?.WriteLine($"[{Time[0]}] === POSITION NOW FLAT === Avg={averagePrice:F2}");
                    string longOrder = _activeLongPullbackSignal != null ? _activeLongPullbackSignal : "NULL";
                    string shortOrder = _activeShortPullbackSignal != null ? _activeShortPullbackSignal : "NULL";
                    _logWriter?.WriteLine($"[{Time[0]}] >> Active PB Orders BEFORE clear: Long={longOrder}, Short={shortOrder}");
                }
                    
                _activeLongSetups.Clear();
                _activeShortSetups.Clear();
                _mostRecentLongSetup = null;
                _mostRecentShortSetup = null;
                _prevRelation = BarRelation.None;
                _stallRelationStreak = 0;
                _activeLongPullbackOrder = null;
                _activeShortPullbackOrder = null;
                _activeLongPullbackSignal = null;
                _activeShortPullbackSignal = null;
            }
        }
        #endregion
    }
}