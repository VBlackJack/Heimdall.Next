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

using Heimdall.App.ViewModels.Dialogs;
using Heimdall.Core.Security;

namespace Heimdall.App.Tests;

public sealed class PinSetupDialogViewModelTests
{
    [Fact]
    public void Submit_FirstTimeSet_CompletesWithVerifiableHash()
    {
        PinManager pinManager = new PinManager();
        PinSetupDialogViewModel viewModel = new PinSetupDialogViewModel(pinManager, null, null);

        viewModel.SubmitCommand.Execute(new PinSetupInput(null, "0000", "0000"));

        Assert.NotNull(viewModel.Result);
        PinSetupResult result = viewModel.Result!;
        Assert.NotNull(result.Hash);
        Assert.NotNull(result.Salt);
        string hash = result.Hash!;
        string salt = result.Salt!;
        Assert.Equal(PinSetupOutcome.Set, result.Outcome);
        Assert.True(pinManager.Verify("0000", hash, salt));
        Assert.True(viewModel.IsCompleted);
        Assert.Null(viewModel.Error);
    }

    [Fact]
    public void Submit_ConfirmMismatch_SetsError()
    {
        PinManager pinManager = new PinManager();
        PinSetupDialogViewModel viewModel = new PinSetupDialogViewModel(pinManager, null, null);

        viewModel.SubmitCommand.Execute(new PinSetupInput(null, "0000", "1111"));

        Assert.Equal(PinSetupError.ConfirmMismatch, viewModel.Error);
        Assert.Null(viewModel.Result);
        Assert.False(viewModel.IsCompleted);
    }

    [Fact]
    public void Submit_TooShort_SetsError()
    {
        PinManager pinManager = new PinManager();
        PinSetupDialogViewModel viewModel = new PinSetupDialogViewModel(pinManager, null, null);

        viewModel.SubmitCommand.Execute(new PinSetupInput(null, "123", "123"));

        Assert.Equal(PinSetupError.PinTooShort, viewModel.Error);
        Assert.Null(viewModel.Result);
    }

    [Fact]
    public void Submit_NonDigit_SetsError()
    {
        PinManager pinManager = new PinManager();
        PinSetupDialogViewModel viewModel = new PinSetupDialogViewModel(pinManager, null, null);

        viewModel.SubmitCommand.Execute(new PinSetupInput(null, "12ab", "12ab"));

        Assert.Equal(PinSetupError.PinInvalidChars, viewModel.Error);
        Assert.Null(viewModel.Result);
    }

    [Fact]
    public void Submit_ExistingPinWrongCurrent_SetsError()
    {
        PinManager pinManager = new PinManager();
        StoredPin storedPin = CreateStoredPin(pinManager);
        PinSetupDialogViewModel viewModel = new PinSetupDialogViewModel(pinManager, storedPin.Hash, storedPin.Salt);

        viewModel.SubmitCommand.Execute(new PinSetupInput("9999", "5678", "5678"));

        Assert.Equal(PinSetupError.WrongCurrentPin, viewModel.Error);
        Assert.Null(viewModel.Result);
    }

    [Fact]
    public void Submit_ExistingPinCorrectCurrent_CompletesWithNewHash()
    {
        PinManager pinManager = new PinManager();
        StoredPin storedPin = CreateStoredPin(pinManager);
        PinSetupDialogViewModel viewModel = new PinSetupDialogViewModel(pinManager, storedPin.Hash, storedPin.Salt);

        viewModel.SubmitCommand.Execute(new PinSetupInput("1234", "5678", "5678"));

        Assert.NotNull(viewModel.Result);
        PinSetupResult result = viewModel.Result!;
        Assert.NotNull(result.Hash);
        Assert.NotNull(result.Salt);
        string hash = result.Hash!;
        string salt = result.Salt!;
        Assert.Equal(PinSetupOutcome.Set, result.Outcome);
        Assert.True(pinManager.Verify("5678", hash, salt));
        Assert.True(viewModel.IsCompleted);
    }

    [Fact]
    public void Remove_CorrectCurrent_CompletesWithRemovedOutcome()
    {
        PinManager pinManager = new PinManager();
        StoredPin storedPin = CreateStoredPin(pinManager);
        PinSetupDialogViewModel viewModel = new PinSetupDialogViewModel(pinManager, storedPin.Hash, storedPin.Salt);

        viewModel.RemoveCommand.Execute("1234");

        Assert.NotNull(viewModel.Result);
        PinSetupResult result = viewModel.Result!;
        Assert.Equal(PinSetupOutcome.Removed, result.Outcome);
        Assert.Null(result.Hash);
        Assert.Null(result.Salt);
        Assert.True(viewModel.IsCompleted);
    }

    [Fact]
    public void Remove_WrongCurrent_SetsError()
    {
        PinManager pinManager = new PinManager();
        StoredPin storedPin = CreateStoredPin(pinManager);
        PinSetupDialogViewModel viewModel = new PinSetupDialogViewModel(pinManager, storedPin.Hash, storedPin.Salt);

        viewModel.RemoveCommand.Execute("0000");

        Assert.Equal(PinSetupError.WrongCurrentPin, viewModel.Error);
        Assert.Null(viewModel.Result);
        Assert.False(viewModel.IsCompleted);
    }

    private static StoredPin CreateStoredPin(PinManager pinManager)
    {
        string salt = pinManager.GenerateSalt();
        string hash = pinManager.Hash("1234", salt);

        return new StoredPin(hash, salt);
    }

    private sealed record StoredPin(string Hash, string Salt);
}
