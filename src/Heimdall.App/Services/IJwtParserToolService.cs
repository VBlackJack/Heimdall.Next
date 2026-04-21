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

using Heimdall.Core.Jwt;

namespace Heimdall.App.Services;

public interface IJwtParserToolService
{
    bool TryDecode(string? input, out JwtDecoded? decoded, out JwtDecodeError error);
    JwtClaimsEvaluation EvaluateExpiration(string payloadJson, DateTimeOffset now);
    JwtAlgorithmKind ClassifyAlgorithm(string? alg);
    JwtHmacVerificationResult VerifyHmac(JwtDecoded decoded, JwtAlgorithmKind alg, string secret);
}
