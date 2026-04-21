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

namespace Heimdall.Core.Ssh;

/// <summary>
/// Result of parsing an OpenSSH config file.
/// </summary>
/// <param name="Candidates">Importable candidates discovered in the file.</param>
/// <param name="Diagnostics">Structured non-fatal diagnostics emitted during parsing.</param>
public sealed record OpenSshParseResult(
    IReadOnlyList<OpenSshImportCandidate> Candidates,
    IReadOnlyList<OpenSshImportDiagnostic> Diagnostics);
