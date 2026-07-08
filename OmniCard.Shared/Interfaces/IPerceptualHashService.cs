using System.IO;
using OmniCard.Models;

namespace OmniCard.Interfaces;

public interface IPerceptualHashService
{
    ulong ComputeHash(Stream imageStream, Action<HashStageResult>? onStage = null);
    ulong[] ComputeArtHash(Stream imageStream, (double X, double Y, double W, double H)[] cropRegions, Action<HashStageResult>? onStage = null);
}
