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

namespace Heimdall.App.Services;

public enum ResolutionChoiceKind
{
    MatchWindow,
    Fixed,
    Custom,
    SaveAsDefaultForServer
}

public sealed record ResolutionChoice(ResolutionChoiceKind Kind, int Width = 0, int Height = 0)
{
    public static ResolutionChoice MatchWindow { get; } = new(ResolutionChoiceKind.MatchWindow);

    public static ResolutionChoice Custom { get; } = new(ResolutionChoiceKind.Custom);

    public static ResolutionChoice SaveAsDefaultForServer { get; } = new(ResolutionChoiceKind.SaveAsDefaultForServer);

    public static ResolutionChoice Fixed(int width, int height) => new(ResolutionChoiceKind.Fixed, width, height);
}
