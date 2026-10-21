using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows.Forms;

namespace ModbusEmulatorX {
    [Flags]
    public enum LiveCellChanges { None = 0, Value = 1, Simulation = 2 }

                                                                           
                                                                             
    public sealed class LiveCellTracker {
        readonly Dictionary<Point, double> values = new Dictionary<Point, double>();
        readonly Dictionary<Point, Wave> modes = new Dictionary<Point, Wave>();

        public LiveCellChanges Observe(Point point, bool valueVisible, bool modeVisible) {
            LiveCellChanges changes = LiveCellChanges.None;
            if (valueVisible) {
                double previous;
                if (!values.TryGetValue(point, out previous) || !previous.Equals(point.Value)) {
                    values[point] = point.Value;
                    changes |= LiveCellChanges.Value;
                }
            }
            if (modeVisible) {
                Wave previous;
                if (!modes.TryGetValue(point, out previous) || previous != point.Simulation) {
                    modes[point] = point.Simulation;
                    changes |= LiveCellChanges.Simulation;
                }
            }
            return changes;
        }

        public void Clear() { values.Clear(); modes.Clear(); }
    }

    public sealed class LivePointGrid : DataGridView {
        readonly LiveCellTracker changes = new LiveCellTracker();
        long refreshAfter;

        public LivePointGrid() {
            DoubleBuffered = true;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
            DataSourceChanged += (sender, args) => changes.Clear();
            DataBindingComplete += (sender, args) => changes.Clear();
        }

        protected override void OnScroll(ScrollEventArgs args) {
                                                                                 
                                                                                        
            refreshAfter = Stopwatch.GetTimestamp() + Stopwatch.Frequency / 5;
            base.OnScroll(args);
        }

        public int RefreshLiveCells() {
            if (IsDisposed || !IsHandleCreated || !Visible || Stopwatch.GetTimestamp() < refreshAfter)
                return 0;

            DataGridViewColumn valueColumn = Columns["Value"];
            DataGridViewColumn modeColumn = Columns["Simulation"];
            bool valueVisible = valueColumn != null && valueColumn.Displayed;
            bool modeVisible = modeColumn != null && modeColumn.Displayed;
            if (!valueVisible && !modeVisible) return 0;

            int invalidated = 0;
            for (int index = Rows.GetFirstRow(DataGridViewElementStates.Displayed);
                 index >= 0;
                 index = Rows.GetNextRow(index, DataGridViewElementStates.Displayed)) {
                Point point = Rows[index].DataBoundItem as Point;
                if (point == null) continue;
                LiveCellChanges changed = changes.Observe(point, valueVisible, modeVisible);
                if ((changed & LiveCellChanges.Value) != 0) {
                    InvalidateCell(valueColumn.Index, index);
                    invalidated++;
                }
                if ((changed & LiveCellChanges.Simulation) != 0) {
                    InvalidateCell(modeColumn.Index, index);
                    invalidated++;
                }
            }
            return invalidated;
        }
    }

    public sealed class BufferedTrendPanel : Panel {
        public BufferedTrendPanel() {
            DoubleBuffered = true;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }
    }
}
