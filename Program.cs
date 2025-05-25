using System;
using System.Windows.Forms;
using VibrationAnalysis.UI;

namespace VibrationAnalysis
{
    static class Program
    {
        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new RotorBalancingForm());
        }
    }
}