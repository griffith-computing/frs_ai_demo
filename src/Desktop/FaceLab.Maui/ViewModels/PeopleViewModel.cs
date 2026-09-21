//----------------------------------------------------------------------------------
// THIS CODE AND INFORMATION ARE PROVIDED "AS IS" WITHOUT WARRANTY OF ANY KIND,
// EITHER EXPRESSED OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE IMPLIED WARRANTIES
// OF MERCHANTABILITY AND/OR FITNESS FOR A PARTICULAR PURPOSE.
//
// This sample is not supported under any Microsoft standard support program or
// service. It is provided to you solely for the purpose of illustration and is
// intended to be modified, tested, and validated by the customer prior to any
// production use. The entire risk arising out of the use or performance of this
// code remains with the customer.
//
// Copyright (c) Microsoft Corporation. All rights reserved.
//----------------------------------------------------------------------------------

using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FaceLab.Core.Data;

namespace FaceLab.Maui.ViewModels;

/// <summary>The locally tracked person directory - the desktop analog of the Cosmos Faces container.</summary>
public sealed partial class PeopleViewModel : ObservableObject
{
    private readonly IFaceLabRepository _repository;

    [ObservableProperty]
    public partial string StatusMessage { get; set; }

    public PeopleViewModel(IFaceLabRepository repository)
    {
        _repository = repository;
        StatusMessage = string.Empty;
    }

    public ObservableCollection<PersonRecord> People { get; } = new();

    [RelayCommand]
    public async Task LoadAsync()
    {
        var people = await _repository.GetPeopleAsync(CancellationToken.None);
        People.Clear();
        foreach (var person in people)
        {
            People.Add(person);
        }

        StatusMessage = People.Count == 0
            ? "No people seen yet. Faces enrolled or matched during a run appear here."
            : $"{People.Count} person record(s) tracked locally.";
    }
}
