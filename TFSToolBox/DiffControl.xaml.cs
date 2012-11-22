using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace TFSToolBox
{
    /// <summary>
    /// Interaction logic for DiffControl.xaml
    /// </summary>
    public partial class DiffControl : UserControl
    {
        public DiffControl()
        {
            InitializeComponent();
        }

        public void SetDiffText(string diffText)
        {
            var paragraphResult = new Paragraph();
            var lines = diffText.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

            var typeface = new Typeface("Consolas");

            var maxLineWidth = 0.0;

            foreach (var line in lines)
            {
                var run = GetRunOfLine(line);
                var textWidth = new FormattedText(line, System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight, typeface, 12, run.Foreground).Width;
                maxLineWidth = Math.Max(maxLineWidth, textWidth);

                paragraphResult.Inlines.Add(run);
                paragraphResult.Inlines.Add(new LineBreak());
            }

            DiffDocument.Blocks.Clear();
            DiffDocument.Blocks.Add(paragraphResult);

            DiffDocument.MinPageWidth = maxLineWidth + 35;
        }

        private Run GetRunOfLine(string line)
        {
            var result = new Run(line);
            result.Foreground = new SolidColorBrush(Color.FromRgb(102, 102, 102));

            if (line.StartsWith("@@"))
            {
                result.Background = new SolidColorBrush(Color.FromRgb(234, 232, 245));
                result.Foreground = new SolidColorBrush(Color.FromRgb(153, 153, 153));
            }
            else if (line.StartsWith("+"))
            {
                result.Background = new SolidColorBrush(Color.FromRgb(221, 255, 221));
                result.Foreground = new SolidColorBrush(Color.FromRgb(0, 20, 0));
            }
            else if (line.StartsWith("-"))
            {
                result.Background = new SolidColorBrush(Color.FromRgb(255, 221, 221));
                result.Foreground = new SolidColorBrush(Color.FromRgb(116, 0, 0));
            }

            return result;
        }
    }
}
