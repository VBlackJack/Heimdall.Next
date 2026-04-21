/*
 * Copyright 2026 Julien Bombled
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 *
 *     http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */

using System.Text.RegularExpressions;

namespace Heimdall.Core.Matching;

public enum RegexTestStatus
{
    Success,
    EmptyPattern,
    InvalidPattern,
    MatchTimeout,
}

public readonly record struct RegexGroupInfo(
    int Index,
    string Name,
    int StartIndex,
    int Length,
    string Value,
    bool IsNamed);

public readonly record struct RegexMatchInfo(
    int Index,
    int Length,
    string Value,
    IReadOnlyList<RegexGroupInfo> Groups);

public readonly record struct RegexTestResult(
    RegexTestStatus Status,
    int TotalMatchCount,
    IReadOnlyList<RegexMatchInfo> Matches,
    string ErrorMessage);

public static class RegexEngine
{
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(1);

    public static RegexTestResult Test(string pattern, string input, RegexOptions options, TimeSpan? timeout = null)
    {
        ArgumentNullException.ThrowIfNull(pattern);
        ArgumentNullException.ThrowIfNull(input);

        if (string.IsNullOrEmpty(pattern))
        {
            return new RegexTestResult(RegexTestStatus.EmptyPattern, 0, [], string.Empty);
        }

        try
        {
            var regex = new Regex(pattern, options, timeout ?? DefaultTimeout);
            if (string.IsNullOrEmpty(input))
            {
                return new RegexTestResult(RegexTestStatus.Success, 0, [], string.Empty);
            }

            var matches = regex.Matches(input);
            var matchInfos = new List<RegexMatchInfo>(matches.Count);
            foreach (Match match in matches)
            {
                var groups = new List<RegexGroupInfo>(Math.Max(0, match.Groups.Count - 1));
                for (var groupIndex = 1; groupIndex < match.Groups.Count; groupIndex++)
                {
                    var group = match.Groups[groupIndex];
                    var name = regex.GroupNameFromNumber(groupIndex);
                    groups.Add(new RegexGroupInfo(
                        groupIndex,
                        name,
                        group.Success ? group.Index : -1,
                        group.Length,
                        group.Value,
                        !int.TryParse(name, out _)));
                }

                matchInfos.Add(new RegexMatchInfo(match.Index, match.Length, match.Value, groups));
            }

            return new RegexTestResult(RegexTestStatus.Success, matches.Count, matchInfos, string.Empty);
        }
        catch (ArgumentException ex)
        {
            return new RegexTestResult(RegexTestStatus.InvalidPattern, 0, [], ex.Message);
        }
        catch (RegexMatchTimeoutException ex)
        {
            return new RegexTestResult(RegexTestStatus.MatchTimeout, 0, [], ex.Message);
        }
    }
}
