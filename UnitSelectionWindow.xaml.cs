
using SpeakingTrainer.Core;

using System.Windows;
using System.Windows.Controls;

namespace SpeakingTrainer
{
    public partial class UnitSelectionWindow : Window
    {
        // 사용자가 선택한 유닛 / Selected learning unit
        public UnitEntry? SelectedUnit { get; private set; }

        public UnitSelectionWindow(ContentCatalog catalog)
        {
            InitializeComponent();

            // 챕터 목록 표시 / Display chapters
            ChapterList.ItemsSource = catalog.Chapters;

            if (catalog.Chapters.Count > 0)
                ChapterList.SelectedIndex = 0;
        }


        // 챕터 선택 시 유닛 목록 갱신 / Update units on chapter selection
        private void ChapterList_SelectionChanged(
            object sender,
            SelectionChangedEventArgs e)
        {
            if (ChapterList.SelectedItem is ChapterEntry chapter)
            {
                UnitList.ItemsSource = chapter.Units;

                if (chapter.Units.Count > 0)
                    UnitList.SelectedIndex = 0;
            }
            else
            {
                UnitList.ItemsSource = null;
            }
        }


        // 유닛 선택 여부 확인 / Check unit selection
        private void UnitList_SelectionChanged(
            object sender,
            SelectionChangedEventArgs e)
        {
            StartButton.IsEnabled =
                UnitList.SelectedItem is UnitEntry;
        }


        // 선택한 유닛 전달 / Return the selected unit
        private void StartButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            SelectedUnit = UnitList.SelectedItem as UnitEntry;

            if (SelectedUnit == null)
                return;

            DialogResult = true;
        }
    }
}
