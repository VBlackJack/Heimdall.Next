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
using Heimdall.Core.Matching;

namespace Heimdall.App.Services;

public sealed class RegexTesterToolService : IRegexTesterToolService
{
    public RegexTestResult Test(string pattern, string input, bool ignoreCase, bool multiline, bool singleline)
    {
        var options = RegexOptions.None;
        if (ignoreCase) { options |= RegexOptions.IgnoreCase; }
        if (multiline) { options |= RegexOptions.Multiline; }
        if (singleline) { options |= RegexOptions.Singleline; }

        return RegexEngine.Test(pattern, input, options);
    }
}
