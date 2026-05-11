using Nupal.Domain.Entities;
using NUPAL.Core.Application.DTOs;
using NUPAL.Core.Infrastructure.Services.Scheduling.Models;

namespace NUPAL.Core.Infrastructure.Services.Scheduling
{

    internal static class SchedulingRecommender
    {
        private const double BlendSimilarity  = 0.25;
        private const double BlendCoverage    = 0.55;
        private const double BlendCompactness = 0.20;
        private const double WCourses     = 4.0;
        private const double WInstructors = 2.5;
        private const double WDays        = 1.5;
        private const double WTimeSlots   = 1.0;

        private const double DiversityThreshold = 0.85;
        internal static BlockVocab BuildVocab(IEnumerable<BlockFeatures> features)
        {
            var allCourses     = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            var allInstructors = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var f in features)
            {
                foreach (var c in f.Courses)     allCourses.Add(c);
                foreach (var i in f.Instructors) allInstructors.Add(i);
            }

            var courses     = allCourses.ToList();
            var instructors = allInstructors.ToList();

            return new BlockVocab
            {
                Courses       = courses,
                Instructors   = instructors,
                CourseIdx     = courses.Select((c, i) => (c, i))
                                       .ToDictionary(x => x.c, x => x.i, StringComparer.OrdinalIgnoreCase),
                InstructorIdx = instructors.Select((c, i) => (c, i))
                                           .ToDictionary(x => x.c, x => x.i, StringComparer.OrdinalIgnoreCase),
            };
        }

        private static int VecLen(BlockVocab vocab) =>
            vocab.Courses.Count + vocab.Instructors.Count
            + SchedulingTimeHelper.DaysOrder.Length + SchedulingTimeHelper.NumSlots;
        internal static double[] VectoriseBlock(BlockFeatures f, BlockVocab vocab)
        {
            var v = new double[VecLen(vocab)];
            int offset = 0;

            foreach (var c in f.Courses)
                if (vocab.CourseIdx.TryGetValue(c, out var idx)) v[offset + idx] = 1;
            offset += vocab.Courses.Count;

            foreach (var i in f.Instructors)
                if (vocab.InstructorIdx.TryGetValue(i, out var idx)) v[offset + idx] = 1;
            offset += vocab.Instructors.Count;

            foreach (var d in f.Days)
                if (SchedulingTimeHelper.DayToIdx.TryGetValue(d, out var idx)) v[offset + idx] = 1;
            offset += SchedulingTimeHelper.DaysOrder.Length;

            foreach (var s in f.AllSlots)
                if (s >= 0 && s < SchedulingTimeHelper.NumSlots) v[offset + s] = 1;

            return v;
        }

        internal static (double[] pv, double[] wv) VectorisePrefs(
            SchedulePreferencesDto prefs,
            IEnumerable<string> desiredCourses,
            BlockVocab vocab,
            bool matchCoursesOnly = false)
        {
            int nc    = vocab.Courses.Count;
            int ni    = vocab.Instructors.Count;
            int total = VecLen(vocab);

            var pv = new double[total];
            var wv = Enumerable.Repeat(1.0, total).ToArray();
            int offset = 0;

            var vocabSanitized = vocab.CourseIdx
                .Select(kv => new { KeySanitized = new string(kv.Key.ToLower().Where(char.IsLetterOrDigit).ToArray()), kv.Value })
                .ToList();

            foreach (var course in desiredCourses)
            {
                if (string.IsNullOrWhiteSpace(course)) continue;
                var inputKey = new string(course.ToLower().Where(char.IsLetterOrDigit).ToArray());
                
                foreach (var vocabItem in vocabSanitized)
                {
                    if (vocabItem.KeySanitized.Contains(inputKey) || inputKey.Contains(vocabItem.KeySanitized))
                        pv[offset + vocabItem.Value] = 1;
                }
            }
            for (int i = offset; i < offset + nc; i++) wv[i] = WCourses;
            offset += nc;

            double effectiveWInstr = matchCoursesOnly ? 0.0 : WInstructors;
            foreach (var instr in prefs.PreferredInstructors ?? [])
            {
                var key = instr.ToLower().Trim();
                if (vocab.InstructorIdx.TryGetValue(key, out var idx))
                    pv[offset + idx] = 1;
            }
            for (int i = offset; i < offset + ni; i++) wv[i] = effectiveWInstr;
            offset += ni;

            double effectiveWDays = matchCoursesOnly ? 0.0 : WDays;
            if (prefs.DayMode == "specific")
            {
                foreach (var day in prefs.PreferredDays ?? [])
                    if (SchedulingTimeHelper.DayToIdx.TryGetValue(day, out var idx))
                        pv[offset + idx] = 1;
            }
            for (int i = offset; i < offset + SchedulingTimeHelper.DaysOrder.Length; i++) wv[i] = effectiveWDays;
            offset += SchedulingTimeHelper.DaysOrder.Length;

            double effectiveWTime = matchCoursesOnly ? 0.0 : WTimeSlots;
            var tsSlots = SchedulingTimeHelper.TimeSlots(
                prefs.EarliestTime ?? "07:00",
                prefs.LatestTime   ?? "21:00");
            foreach (var s in tsSlots) pv[offset + s] = 1;
            for (int i = offset; i < offset + SchedulingTimeHelper.NumSlots; i++) wv[i] = effectiveWTime;

            return (pv, wv);
        }


        internal static ScoredBlock ScoreBlock(
            RawBlockDto raw,
            BlockFeatures f,
            IEnumerable<string> desiredCourses,
            double[] prefVec,
            double[] weightVec,
            BlockVocab vocab,
            SchedulePreferencesDto prefs,
            bool matchCoursesOnly = false,
            int originalDesiredCount = 0)
        {
            var bvec = VectoriseBlock(f, vocab);
            var sim  = WeightedCosine(prefVec, bvec, weightVec);
            var cov  = Coverage(desiredCourses, f, originalDesiredCount);
            var comp = matchCoursesOnly ? 1.0 : Compactness(f);
            var dayB = matchCoursesOnly ? 1.0 : (prefs.DayMode == "count"
                ? DayCountScore(f, prefs.NumPreferredDays)
                : 1.0);

            var combined = matchCoursesOnly 
                ? (BlendSimilarity * sim + BlendCoverage * cov) / (BlendSimilarity + BlendCoverage)
                : BlendSimilarity * sim + BlendCoverage * cov + BlendCompactness * comp;

            return new ScoredBlock
            {
                BlockId     = f.BlockId,
                Level       = f.Level,
                FinalScore  = combined * dayB,
                Similarity  = sim,
                Coverage    = cov,
                Compactness = comp,
                DayBonus    = dayB,
                NumDays     = f.NumDays,
                TotalHours  = f.TotalHours,
                MaxGapH     = CalcMaxGapHours(f),
                Courses     = f.Courses.Select(SchedulingTimeHelper.TitleCase).ToList(),
                Instructors = f.Instructors.Select(SchedulingTimeHelper.TitleCase).ToList(),
                Days        = f.Days
                    .OrderBy(d => SchedulingTimeHelper.DayToIdx.GetValueOrDefault(d, 9))
                    .ToList(),
                Raw = raw,
            };
        }


        internal static bool PassesHardConstraints(BlockFeatures f, SchedulePreferencesDto prefs, bool matchCoursesOnly = false)
        {
            if (matchCoursesOnly) return true;
            if (f.NumDays > prefs.MaxDaysPerWeek) return false;
            if (prefs.MaxGapHours > 0 && CalcMaxGapHours(f) > prefs.MaxGapHours) return false;
            return true;
        }


        internal static List<ScoredBlock> DiversityFilter(
            List<ScoredBlock> candidates,
            Dictionary<string, double[]> vecs,
            int topN)
        {
            var selected     = new List<ScoredBlock>();
            var selectedVecs = new List<double[]>();

            foreach (var c in candidates)
            {
                if (!vecs.TryGetValue(c.BlockId, out var vec)) continue;

                bool tooSimilar = selectedVecs
                    .Select((sv, selIdx) =>
                    {
                        double dot = 0, nA = 0, nB = 0;
                        for (int i = 0; i < vec.Length; i++)
                        {
                            dot += vec[i] * sv[i];
                            nA  += vec[i] * vec[i];
                            nB  += sv[i] * sv[i];
                        }
                        double d = Math.Sqrt(nA) * Math.Sqrt(nB);
                        if (d <= 0 || dot / d < 0.98) return false; // Increased threshold to 0.98

                        // Only suppress if it's literally the same content AND same coverage
                        // But allow different BlockIds to coexist
                        return c.BlockId == selected[selIdx].BlockId || c.Coverage < selected[selIdx].Coverage;
                    })
                    .Any(suppress => suppress);

                if (!tooSimilar)
                {
                    selected.Add(c);
                    selectedVecs.Add(vec);
                }

                if (selected.Count >= topN) break;
            }

            // Pad with next-best candidates if diversity filter was too aggressive
            if (selected.Count < topN)
                selected.AddRange(candidates.Except(selected).Take(topN - selected.Count));

            return selected;
        }

        internal static RecommendationResultDto BuildResultDto(
            ScoredBlock s, SchedulePreferencesDto prefs, List<CourseMapping>? mappings = null)
        {
            int matchScore = (int)Math.Round(s.FinalScore * 100);
            var reasons    = new List<string>();

            if (s.Coverage >= 0.8)
                reasons.Add($"Covers {(int)Math.Round(s.Coverage * 100)}% of your desired courses");
            if (s.Compactness >= 0.7)
                reasons.Add("Compact schedule with minimal gaps");
            if (s.DayBonus >= 0.8 && prefs.DayMode == "count")
                reasons.Add($"Matches your preferred {prefs.NumPreferredDays}-day schedule");
            if (s.Similarity >= 0.5 && (prefs.PreferredInstructors?.Count ?? 0) > 0)
                reasons.Add("Includes preferred instructor(s)");
            if (s.NumDays <= 3)
                reasons.Add($"{s.NumDays}-day campus schedule");
            if (s.MaxGapH <= 1)
                reasons.Add("No long gaps between sessions");
            if (reasons.Count == 0)
                reasons.Add($"Block {s.BlockId} — Score: {matchScore}");

            return new RecommendationResultDto
            {
                Block      = SchedulingBlockMapper.RawToFrontend(s.Raw, mappings),
                MatchScore = matchScore,
                Reasons    = reasons,
                Breakdown  = new ScoreBreakdownDto
                {
                    Similarity  = Math.Round(s.Similarity  * 100) / 100,
                    Coverage    = Math.Round(s.Coverage    * 100) / 100,
                    Compactness = Math.Round(s.Compactness * 100) / 100,
                    DayBonus    = Math.Round(s.DayBonus    * 100) / 100,
                },
            };
        }

        private static double WeightedCosine(double[] a, double[] b, double[] w)
        {
            double dot = 0, normA = 0, normB = 0;
            for (int i = 0; i < a.Length; i++)
            {
                double wa = a[i] * w[i], wb = b[i] * w[i];
                dot   += wa * wb;
                normA += wa * wa;
                normB += wb * wb;
            }
            double denom = Math.Sqrt(normA) * Math.Sqrt(normB);
            return denom > 0 ? dot / denom : 0;
        }


        private static double Coverage(IEnumerable<string> desiredCourses, BlockFeatures f, int originalDesiredCount)
        {
            if (originalDesiredCount <= 0) return 0.0;

            var desired = desiredCourses
                .Where(c => !string.IsNullOrWhiteSpace(c))
                .Select(c => new string(c.ToLower().Where(char.IsLetterOrDigit).ToArray()))
                .Distinct()
                .ToList();
                
            // 1. Primary Match: Match by normalized codes
            int matchedByCode = f.NormalizedCourseCodes.Count(code => 
                desired.Contains(new string(code.ToLower().Where(char.IsLetterOrDigit).ToArray()))
            );

            // 2. Fallback Match: Match by raw course names (for courses without mappings)
            var fCoursesSanitized = f.Courses
                .Select(c => new string(c.ToLower().Where(char.IsLetterOrDigit).ToArray()))
                .ToList();

            int matchedByName = 0;
            foreach (var bc in fCoursesSanitized)
            {
                if (desired.Any(d => bc.Contains(d) || d.Contains(bc)))
                {
                    matchedByName++;
                }
            }

            // Use the best result
            int finalMatched = Math.Max(matchedByCode, matchedByName);
            
            // Cap at original count to avoid > 100% scores due to alias overlapping
            finalMatched = Math.Min(finalMatched, originalDesiredCount);

            return (double)finalMatched / originalDesiredCount;
        }

        private static double Compactness(BlockFeatures f)
        {
            int totalIdle = 0;
            foreach (var kv in f.DaySlots)
            {
                var slots = kv.Value;
                if (slots.Count < 2) continue;
                int span   = slots[^1] - slots[0] + 1;
                int unique = slots.Distinct().Count();
                totalIdle += span - unique;
            }
            return 1.0 / (1 + 0.1 * totalIdle); // Softened from 1.0
        }


        private static double CalcMaxGapHours(BlockFeatures f)
        {
            double max = 0;
            foreach (var kv in f.DaySlots)
            {
                var slots = kv.Value;
                for (int i = 0; i < slots.Count - 1; i++)
                {
                    double gap = (slots[i + 1] - slots[i] - 1)
                                 * SchedulingTimeHelper.SlotSizeMin / 60.0;
                    if (gap > max) max = gap;
                }
            }
            return max;
        }

        private static double DayCountScore(BlockFeatures f, int? numPreferred) =>
            numPreferred.HasValue
                ? Math.Pow(0.9, Math.Abs(f.NumDays - numPreferred.Value)) // Gradual penalty (90% per day diff)
                : 1.0;
    }
}
