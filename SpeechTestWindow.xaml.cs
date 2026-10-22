
using System;
using System.Windows;
using SpeakingTrainer.Core;
using SpeakingTrainer.Recognition.Windows;

namespace SpeakingTrainer
{
    public partial class SpeechTestWindow : Window
    {
        private WindowsSpeechRecognizer? recognizer;

        public SpeechTestWindow()
        {
            InitializeComponent();
        }

        private void StartButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // 음성 인식기 생성 / Create speech recognizer
                recognizer = new WindowsSpeechRecognizer();

                recognizer.PartialRecognized += OnPartialRecognized;
                recognizer.FinalRecognized += OnFinalRecognized;

                recognizer.Start();

                StatusText.Text = "Listening...";
                StartButton.IsEnabled = false;
                StopButton.IsEnabled = true;
            }
            catch (Exception ex)
            {
                ReleaseRecognizer();
                StatusText.Text = $"Error: {ex.Message}";
            }
        }

        private void OnPartialRecognized(
            object? sender,
            SpeechTextEventArgs e)
        {
            // UI 스레드에서 표시 / Update on UI thread
            Dispatcher.BeginInvoke(new Action(() =>
            {
                PartialText.Text = $"Partial: {e.Text}";
            }));
        }

        private void OnFinalRecognized(
            object? sender,
            SpeechTextEventArgs e)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                ResultsText.AppendText(e.Text + Environment.NewLine);
                ResultsText.ScrollToEnd();
                PartialText.Text = "";
            }));
        }

        private void StopButton_Click(object sender, RoutedEventArgs e)
        {
            ReleaseRecognizer();

            StatusText.Text = "Stopped";
            StartButton.IsEnabled = true;
            StopButton.IsEnabled = false;
        }

        private void ReleaseRecognizer()
        {
            if (recognizer == null)
                return;

            recognizer.PartialRecognized -= OnPartialRecognized;
            recognizer.FinalRecognized -= OnFinalRecognized;

            recognizer.Dispose();
            recognizer = null;
        }

        protected override void OnClosed(EventArgs e)
        {
            ReleaseRecognizer();
            base.OnClosed(e);
        }
    }
}
