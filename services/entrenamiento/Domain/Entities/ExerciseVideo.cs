using Domain.Common;
using Domain.Exceptions;

namespace Domain.Entities
{
    public class ExerciseVideo : BaseEntity
    {
        public Guid ExerciseId { get; private set; }
        public string VideoUrl { get; private set; } = string.Empty;

        private ExerciseVideo() { }
        public static ExerciseVideo Create(Guid exerciseId, string videoUrl)
        {
            if (exerciseId == Guid.Empty)
                throw new DomainException("El ejercicio es obligatorio.");

            var validated = new YouTubeUrl(videoUrl);

            return new ExerciseVideo
            {
                ExerciseId = exerciseId,
                VideoUrl = validated.Value
            };
        }

        // Actualiza el link del video
        public void UpdateVideoUrl(string videoUrl)
        {
            var validated = new YouTubeUrl(videoUrl);
            VideoUrl = validated.Value;
        }
    }
}