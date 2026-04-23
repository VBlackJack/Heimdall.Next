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

using System.ComponentModel;
using System.Runtime.CompilerServices;
namespace Heimdall.App.ViewModels.Tools;

public enum ArpEntryState
{
    Stable,
    New,
    Changed,
    Gone
}

/// <summary>
/// Represents a single ARP table entry with change tracking and notification support.
/// </summary>
internal sealed class ArpEntry : INotifyPropertyChanged
{
    private string _ip = "";
    private string _mac = "";
    private string _vendor = "";
    private string _status = "";
    private string _statusDisplay = "";
    private ArpEntryState _state;
    private string _firstSeen = "";
    private string _lastSeen = "";
    private string _previousMac = "";

    public string Ip
    {
        get => _ip;
        init { _ip = value; OnPropertyChanged(); }
    }

    public string Mac
    {
        get => _mac;
        set { if (_mac != value) { _mac = value; OnPropertyChanged(); } }
    }

    public string Vendor
    {
        get => _vendor;
        set { if (_vendor != value) { _vendor = value; OnPropertyChanged(); } }
    }

    public string Status
    {
        get => _status;
        set { if (_status != value) { _status = value; OnPropertyChanged(); } }
    }

    public string StatusDisplay
    {
        get => _statusDisplay;
        set { if (_statusDisplay != value) { _statusDisplay = value; OnPropertyChanged(); } }
    }

    public ArpEntryState State
    {
        get => _state;
        set { if (_state != value) { _state = value; OnPropertyChanged(); } }
    }

    public string FirstSeen
    {
        get => _firstSeen;
        init { _firstSeen = value; OnPropertyChanged(); }
    }

    public string LastSeen
    {
        get => _lastSeen;
        set { if (_lastSeen != value) { _lastSeen = value; OnPropertyChanged(); } }
    }

    public string PreviousMac
    {
        get => _previousMac;
        set { if (_previousMac != value) { _previousMac = value; OnPropertyChanged(); } }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
