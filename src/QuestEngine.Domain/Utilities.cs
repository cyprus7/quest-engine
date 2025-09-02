using System.Security.Cryptography;
using System.Text;

namespace QuestEngine.Domain;

public static class WeightedPicker
{
    public static int PickIndex(IReadOnlyList<int> weights, double u01)
    {
        long sum = 0;
        foreach (var w in weights) { if (w < 0) throw new ArgumentException("Negative weight"); sum += w; }
        if (sum <= 0) throw new ArgumentException("All weights are zero");
        var target = u01 * sum;
        long acc = 0;
        for (int i = 0; i < weights.Count; i++)
        {
            acc += weights[i];
            if (target < acc) return i;
        }
        return weights.Count - 1;
    }
}

public static class BonusCombinationId
{
    public static string Compute(IEnumerable<RewardDef> rewards)
    {
        var parts = rewards
            .Select(r => $"{r.Type}|{r.GameId ?? ""}|{(r.Denom?.ToString("0.########") ?? "")}|{r.Amount}")
            .OrderBy(s => s);
        var canonical = string.Join(";", parts);
        using var sha = SHA256.Create();
        var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(canonical));
        return Convert.ToHexString(bytes).ToLowerInvariant()[..32];
    }
}
