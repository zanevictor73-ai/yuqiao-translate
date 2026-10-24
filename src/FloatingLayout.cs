using System;
using System.Collections.Generic;
using System.Windows;

namespace Yuqiao
{
    // Pure DIP geometry. This class never reads or changes a window.
    public static class FloatingLayout
    {
        public static Point ReadingPosition(Rect targetWindow, Rect inputPanel, Size readingSize, Rect workArea)
        {
            if (!ValidRectangle(workArea) || workArea.Width <= 0 || workArea.Height <= 0)
                throw new ArgumentException("工作区大小无效。", "workArea");
            if (!ValidRectangle(targetWindow)) throw new ArgumentException("目标窗口范围无效。", "targetWindow");
            if (readingSize.IsEmpty || !Finite(readingSize.Width) || !Finite(readingSize.Height)
                || readingSize.Width <= 0 || readingSize.Height <= 0)
                throw new ArgumentException("阅读窗口大小无效。", "readingSize");

            double width = readingSize.Width, height = readingSize.Height;
            double preferredX = targetWindow.Right + 10;
            if (preferredX + width > workArea.Right) preferredX = targetWindow.Right - width - 14;
            Point preferred = Clamp(new Point(preferredX, targetWindow.Top + 65), readingSize, workArea);
            if (!ValidRectangle(inputPanel) || inputPanel.Width <= 0 || inputPanel.Height <= 0) return preferred;
            Rect obstacle = inputPanel;
            obstacle.Inflate(8, 8);
            if (Overlap(preferred, readingSize, obstacle) <= 0) return preferred;

            // Keep the desired twelve-DIP gap; clamping a non-fitting left position
            // could move it back over the input panel, so try it only if it fits.
            Point left = new Point(inputPanel.Left - width - 12, preferred.Y);
            if (Fits(left, readingSize, workArea) && Overlap(left, readingSize, obstacle) <= 0) return left;
            Point above = new Point(ClampX(inputPanel.Left, width, workArea), inputPanel.Top - height - 12);
            if (Fits(above, readingSize, workArea) && Overlap(above, readingSize, obstacle) <= 0) return above;
            Point below = new Point(ClampX(inputPanel.Left, width, workArea), inputPanel.Bottom + 12);
            if (Fits(below, readingSize, workArea) && Overlap(below, readingSize, obstacle) <= 0) return below;

            // If no adjacent placement fits, minimize the obstructed area. For an
            // axis-aligned rectangular obstacle the minimum over allowed positions
            // occurs at an edge/corner; include preferred positions as stable ties.
            List<Point> candidates = new List<Point>();
            candidates.Add(preferred);
            candidates.Add(Clamp(left, readingSize, workArea));
            candidates.Add(Clamp(above, readingSize, workArea));
            candidates.Add(Clamp(below, readingSize, workArea));
            candidates.Add(Clamp(new Point(workArea.Left, workArea.Top), readingSize, workArea));
            candidates.Add(Clamp(new Point(workArea.Right - width, workArea.Top), readingSize, workArea));
            candidates.Add(Clamp(new Point(workArea.Left, workArea.Bottom - height), readingSize, workArea));
            candidates.Add(Clamp(new Point(workArea.Right - width, workArea.Bottom - height), readingSize, workArea));
            Point best = preferred;
            double bestArea = Overlap(best, readingSize, obstacle);
            double bestDistance = 0;
            foreach (Point candidate in candidates)
            {
                double area = Overlap(candidate, readingSize, obstacle);
                double dx = candidate.X - preferred.X, dy = candidate.Y - preferred.Y;
                double distance = dx * dx + dy * dy;
                if (area < bestArea - 0.000001 || (Math.Abs(area - bestArea) <= 0.000001 && distance < bestDistance))
                { best = candidate; bestArea = area; bestDistance = distance; }
            }
            return best;
        }

        private static Point Clamp(Point point, Size size, Rect area)
        {
            return new Point(ClampX(point.X, size.Width, area),
                Math.Max(area.Top, Math.Min(point.Y, Math.Max(area.Top, area.Bottom - size.Height))));
        }

        private static double ClampX(double value, double width, Rect area)
        { return Math.Max(area.Left, Math.Min(value, Math.Max(area.Left, area.Right - width))); }

        private static bool Fits(Point point, Size size, Rect area)
        {
            return point.X >= area.Left && point.Y >= area.Top
                && point.X + size.Width <= area.Right && point.Y + size.Height <= area.Bottom;
        }

        private static double Overlap(Point point, Size size, Rect obstacle)
        {
            double width = Math.Max(0, Math.Min(point.X + size.Width, obstacle.Right) - Math.Max(point.X, obstacle.Left));
            double height = Math.Max(0, Math.Min(point.Y + size.Height, obstacle.Bottom) - Math.Max(point.Y, obstacle.Top));
            return width * height;
        }

        private static bool Finite(double value) { return !double.IsNaN(value) && !double.IsInfinity(value); }
        private static bool ValidRectangle(Rect rectangle)
        {
            return !rectangle.IsEmpty && Finite(rectangle.X) && Finite(rectangle.Y)
                && Finite(rectangle.Width) && Finite(rectangle.Height) && Finite(rectangle.Right) && Finite(rectangle.Bottom);
        }
    }
}
