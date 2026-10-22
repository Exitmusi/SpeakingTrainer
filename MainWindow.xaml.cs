
using SpeakingTrainer.Audio;
using SpeakingTrainer.Core;
using SpeakingTrainer.Recognition.Windows;
using SpeakingTrainer.Recognition.Vosk;
using SpeakingTrainer.Services;

using System;
using System.IO;
using System.Globalization;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace SpeakingTrainer
{
    public partial class MainWindow : Window
    {
        private readonly IContentRepository contentRepository =
            new JsonContentRepository(
                Path.Combine(AppContext.BaseDirectory, "Content"));

        private readonly DrillSessionController drill;

        private LearningUnit? currentUnit;
        private int chunkIndex;
        private string referenceText = "";

        private bool closing;
        private readonly bool legacyStt = Environment.GetEnvironmentVariable("SPEAKINGTRAINER_LEGACY_STT") == "1";
        private readonly Queue<string> acousticHistory = new();
        private double? chunkDistance;
        private readonly VoskModelProvider? voskModel;

        private static readonly Regex WordPattern = new(
            @"[\p{L}\p{N}]+(?:['’][\p{L}\p{N}]+)*",
            RegexOptions.Compiled);

        public MainWindow()
        {
            InitializeComponent();
            if (!legacyStt)
                voskModel = new VoskModelProvider(Path.Combine(AppContext.BaseDirectory,
                    "Models", "vosk-model-small-en-us-0.15"));

            // 학습 제어기 생성 / Create drill controller
            drill = legacyStt ? new DrillSessionController(
                new SoundPlayerAudioPlaybackService(),
                () => new NAudioMicrophoneLevelSource(),
                () => new WindowsSpeechRecognizer(),
                action => Dispatcher.BeginInvoke(action)
            ) : new DrillSessionController(
                new SoundPlayerAudioPlaybackService(),
                () => new NAudioPcmSource(),
                new WavAcousticEvaluator(),
                action => Dispatcher.BeginInvoke(action),
                () => voskModel!.CreateRecognizer());

            AcousticThreshold.IsEnabled = !legacyStt;
            drill.AcousticAttemptEvaluated += Drill_AcousticAttemptEvaluated;
            drill.WordHighlightChanged += Drill_WordHighlightChanged;
            drill.MicrophoneStateChanged += Drill_MicrophoneStateChanged;
            drill.MicrophoneLevelChanged += Drill_MicrophoneLevelChanged;
            if (legacyStt) MicrophoneStatus.Text = "기존 Windows 음성인식 모드";

            drill.StateChanged += Drill_StateChanged;
            drill.SpeechProgressChanged +=
                Drill_SpeechProgressChanged;
            drill.ChunkCompleted += Drill_ChunkCompleted;
            drill.ErrorOccurred += Drill_ErrorOccurred;
        }

        // 앱 시작 / Application startup
        private async void Window_Loaded(
            object sender,
            RoutedEventArgs e)
        {
            Hide();

            try
            {
                if (!legacyStt) drill.StartMicrophoneMonitoring();
                ReplayButton.IsEnabled = false;

                var catalog =
                    await contentRepository.LoadCatalogAsync();

                var selectionWindow =
                    new UnitSelectionWindow(catalog)
                    {
                        Owner = this
                    };

                bool? selected = selectionWindow.ShowDialog();

                if (selected != true ||
                    selectionWindow.SelectedUnit == null)
                {
                    Close();
                    return;
                }

                currentUnit =
                    await contentRepository.LoadUnitAsync(
                        selectionWindow.SelectedUnit.File);

                if (currentUnit.Chunks.Count == 0)
                {
                    throw new InvalidDataException(
                        "The selected unit has no chunks.");
                }

                chunkIndex = 0;

                Show();
                Activate();

                await RunDrillAsync();
            }
            catch (Exception ex)
            {
                if (closing)
                    return;

                MessageBox.Show(
                    ex.Message,
                    "Initialization Error");

                Close();
            }
        }

        // 현재 청크 시작 / Start current chunk
        private async Task RunDrillAsync()
        {
            if (closing || currentUnit == null)
                return;

            var chunk = currentUnit.Chunks[chunkIndex];
            ChunkPosition.Text = $"청크 {chunkIndex + 1} / {currentUnit.Chunks.Count}";
            chunkDistance = chunk.MaximumAcousticDistance;
            UpdateAcousticThreshold();

            referenceText = chunk.Text;

            ChunkText.Text = referenceText;
            RenderProgress(0, 0);

            try
            {
                await drill.StartAsync(
                    chunk.Audio,
                    chunk.Text);
            }
            catch (Exception ex)
            {
                if (closing)
                    return;

                StatusText.Text = "Error";

                MessageBox.Show(
                    ex.Message,
                    "Speaking Trainer Error");
            }
        }

        // 다시 듣기 / Replay current chunk
        private void AcousticThreshold_Changed(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            if (drill != null) UpdateAcousticThreshold();
        }

        private void UpdateAcousticThreshold()
        {
            string value = AcousticThreshold.Text.Trim();
            drill.MaximumAcousticDistance = value.Length == 0 ? chunkDistance :
                double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double threshold)
                && double.IsFinite(threshold) && threshold > 0 ? threshold : null;
        }

        private void Drill_AcousticAttemptEvaluated(AcousticComparison result, double? threshold, bool success)
        {
            if (closing) return;
            string distance = double.IsFinite(result.Distance) ? result.Distance.ToString("F3", CultureInfo.InvariantCulture) : "—";
            string limit = threshold?.ToString("F3", CultureInfo.InvariantCulture) ?? "없음 (측정만)";
            acousticHistory.Enqueue($"청크 {chunkIndex + 1} | 거리 {distance} | 기준 {limit} | " +
                (success ? "통과" : "재시도") + (result.Rejection == null ? "" : $" | {result.Rejection}"));
            while (acousticHistory.Count > 10) acousticHistory.Dequeue();
            AcousticResults.Text = string.Join(Environment.NewLine, acousticHistory);
            AcousticResults.ScrollToEnd();
        }

        private async void ReplayButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            await RunDrillAsync();
        }

        // 청크 완료 후 자동 진행 / Auto-advance
        private async void Drill_ChunkCompleted()
        {
            if (closing || currentUnit == null)
                return;

            if (chunkIndex + 1 >= currentUnit.Chunks.Count)
            {
                StatusText.Text = "Unit completed!";
                ReplayButton.IsEnabled = true;
                return;
            }

            chunkIndex++;

            await RunDrillAsync();
        }

        // STT 진행률 화면 반영 / Update word highlighting
        private void Drill_SpeechProgressChanged(
            int confirmed,
            int preview)
        {
            if (closing)
                return;

            RenderProgress(confirmed, preview);
        }

        // 학습 상태 표시 / Display practice state
        private void Drill_MicrophoneLevelChanged(double rms)
        {
            if (closing) return;
            MicrophoneStatus.Text = $"마이크 켜짐 · 입력 {rms:F4} / 감지 기준 {DrillSessionEngine.ShadowingVoiceThreshold:F4} · 헤드폰 권장";
        }

        private void Drill_MicrophoneStateChanged(bool running)
        {
            if (closing) return;
            MicrophoneStatus.Text = running
                ? "마이크 켜짐 · 음원 재생 중에도 쉐도잉 가능 · 헤드폰 권장"
                : "마이크 꺼짐";
        }

        private void Drill_StateChanged(DrillState state)
        {
            if (closing)
                return;

            switch (state)
            {
                case DrillState.Idle:
                    StatusText.Text = "Ready";
                    ReplayButton.IsEnabled = true;
                    break;

                case DrillState.PlayingReference:
                    StatusText.Text = legacyStt ? "Playing reference..." :
                        drill.IsReferencePlaying ? "음원 재생 중 — 바로 따라 말하세요" : "음원과 인식기 준비 중...";
                    ReplayButton.IsEnabled = false;
                    break;

                case DrillState.WaitingForSpeech:
                    StatusText.Text = "마이크 입력 대기 · 편하게 말씀해 주세요";
                    ReplayButton.IsEnabled = true;
                    break;

                case DrillState.Speaking:
                    StatusText.Text = drill.IsReferencePlaying ? "음원과 함께 말하는 중..." : "Speaking...";
                    ReplayButton.IsEnabled = true;
                    break;

                case DrillState.EvaluatingSpeech:
                    StatusText.Text = drill.IsReferencePlaying ? "발화 확인 중 · 음원 종료 대기" : "Checking...";
                    ReplayButton.IsEnabled = false;
                    break;

                case DrillState.Completed:
                    StatusText.Text = "Completed";
                    ReplayButton.IsEnabled = true;
                    break;
            }
            if (drill.IsReferencePlaying) ReplayButton.IsEnabled = false;
        }

        private void Drill_WordHighlightChanged(WordHighlightUpdate update)
        {
            if (closing) return;
            RecognitionText.Text = string.IsNullOrWhiteSpace(update.RecognizedText)
                ? "인식: —" : "인식: " + update.RecognizedText;
            var confirmed = new HashSet<int>(update.ConfirmedIndices);
            var partial = new HashSet<int>(update.PartialIndices);
            ChunkText.Inlines.Clear();
            int position = 0, index = 0;
            foreach (Match match in WordPattern.Matches(referenceText))
            {
                if (match.Index > position)
                    ChunkText.Inlines.Add(new Run(referenceText.Substring(position, match.Index - position)) { Foreground = Brushes.Gray });
                ChunkText.Inlines.Add(new Run(match.Value)
                {
                    Foreground = confirmed.Contains(index) ? Brushes.DodgerBlue :
                        partial.Contains(index) ? Brushes.DarkOrange : Brushes.Gray
                });
                index++; position = match.Index + match.Length;
            }
            if (position < referenceText.Length)
                ChunkText.Inlines.Add(new Run(referenceText.Substring(position)) { Foreground = Brushes.Gray });
        }

        private void RenderProgress(
            int confirmedCount,
            int previewCount)
        {
            ChunkText.Inlines.Clear();

            int position = 0;
            int wordIndex = 0;

            foreach (Match match in
                     WordPattern.Matches(referenceText))
            {
                // 공백 및 문장부호 유지
                // Preserve spacing and punctuation
                if (match.Index > position)
                {
                    ChunkText.Inlines.Add(
                        new Run(referenceText.Substring(
                            position,
                            match.Index - position))
                        {
                            Foreground = Brushes.Gray
                        });
                }

                Brush color;

                if (wordIndex < confirmedCount)
                {
                    color = Brushes.DodgerBlue;
                }
                else if (wordIndex < previewCount)
                {
                    color = Brushes.DarkOrange;
                }
                else
                {
                    color = Brushes.Gray;
                }

                ChunkText.Inlines.Add(
                    new Run(match.Value)
                    {
                        Foreground = color
                    });

                position = match.Index + match.Length;
                wordIndex++;
            }

            if (position < referenceText.Length)
            {
                ChunkText.Inlines.Add(
                    new Run(referenceText.Substring(position))
                    {
                        Foreground = Brushes.Gray
                    });
            }
        }

        // 오류 처리 / Handle practice errors
        private void Drill_ErrorOccurred(Exception ex)
        {
            if (closing)
                return;

            StatusText.Text = "Error";

            MessageBox.Show(
                ex.Message,
                "Speaking Trainer Error");
        }

        // 프로그램 종료 / Application cleanup
        private void Window_Closed(
            object sender,
            EventArgs e)
        {
            closing = true;

            drill.StateChanged -= Drill_StateChanged;
            drill.AcousticAttemptEvaluated -= Drill_AcousticAttemptEvaluated;
            drill.WordHighlightChanged -= Drill_WordHighlightChanged;
            drill.MicrophoneStateChanged -= Drill_MicrophoneStateChanged;
            drill.MicrophoneLevelChanged -= Drill_MicrophoneLevelChanged;
            drill.SpeechProgressChanged -=
                Drill_SpeechProgressChanged;
            drill.ChunkCompleted -= Drill_ChunkCompleted;
            drill.ErrorOccurred -= Drill_ErrorOccurred;

            drill.Dispose();
            voskModel?.Dispose();
        }
    }
}
