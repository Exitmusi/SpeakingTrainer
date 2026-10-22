
using SpeakingTrainer.Core;

using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

namespace SpeakingTrainer.Services
{
    public sealed class JsonContentRepository : IContentRepository
    {
        private readonly string contentRoot;

        private readonly JsonSerializerOptions options = new()
        {
            PropertyNameCaseInsensitive = true
        };

        public JsonContentRepository(string contentRoot)
        {
            this.contentRoot = contentRoot;
        }

        // 전체 목차 불러오기
        public async Task<ContentCatalog> LoadCatalogAsync()
        {
            string path = Path.Combine(
                contentRoot,
                "catalog.json"
            );

            string json = await File.ReadAllTextAsync(path);

            return JsonSerializer.Deserialize<ContentCatalog>(
                json, options
            ) ?? throw new InvalidDataException(
                "Invalid content catalog."
            );
        }

        // 선택한 유닛 불러오기
        public async Task<LearningUnit> LoadUnitAsync(
            string unitFile)
        {
            string path = Path.GetFullPath(
                Path.Combine(contentRoot, unitFile)
            );

            string json = await File.ReadAllTextAsync(path);

            var unit = JsonSerializer.Deserialize<LearningUnit>(
                json, options
            ) ?? throw new InvalidDataException(
                "Invalid learning unit."
            );

            // 유닛 폴더를 기준으로 음원 경로 해석
            string unitDirectory = Path.GetDirectoryName(path)!;

            foreach (var chunk in unit.Chunks)
            {
                chunk.Audio = Path.GetFullPath(
                    Path.Combine(unitDirectory, chunk.Audio)
                );
            }

            return unit;
        }
    }
}
