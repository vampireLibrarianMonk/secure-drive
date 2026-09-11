using System.IO;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EmergencyArchive.Core;
using EmergencyArchive.Crypto.Vault;
using EmergencyArchive.Integrity;
using EmergencyArchive.Search;
using EmergencyArchive.Sync;

namespace EmergencyArchive.UI;

/// <summary>
/// Estate-planning quick-setup (task 4): a one-click flow that prepares the
/// archive so a spouse, family member, or executor can find and open the
/// owner's important documents after the owner is gone. It:
///   1. writes a plain-language "letter to whoever inherits this drive" as a
///      real document inside the vault (top-level "Estate Plan" folder), so it
///      shows up immediately in the browse screen and is fully encrypted;
///   2. writes a companion, password-free copy into the drive's public\ folder
///      next to RECOVERY-INSTRUCTIONS.txt, so a finder learns what the drive is
///      and how to get help even before they have the password;
///   3. seeds a lightweight "getting started" note in each recommended
///      category folder so the categories exist and the owner sees where each
///      kind of document belongs.
/// The owner still adds their real source folders and runs UPDATE ARCHIVE; this
/// flow removes the blank-page problem and documents the "pass it on" story.
/// </summary>
public sealed partial class SetupViewModel
{
    public const string EstateFolderName = "Estate Plan";
    public const string EstateLetterFileName = "READ-ME-FIRST — Letter to my family.txt";
    public const string EstatePublicFileName = "ESTATE-PLAN-README.txt";

    /// <summary>
    /// Recommended folders an estate archive should contain (spec section 10
    /// categories). These are shown as guidance in the letter and the UI — we
    /// deliberately do NOT create placeholder documents for them, because empty
    /// "about this folder" notes would clutter browse results and pollute search.
    /// The folders appear on their own once the owner files real documents.
    /// </summary>
    private static readonly string[] RecommendedFolders =
    [
        "Identity", "Financial", "Insurance", "Property",
        "Legal", "Medical", "Family",
    ];

    [ObservableProperty] private string? estateOwnerName;

    [ObservableProperty] private string? estateContact;

    /// <summary>The editable letter shown in the estate card and saved verbatim to the vault.</summary>
    [ObservableProperty] private string estateLetterText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasEstateStatus))]
    private string? estateStatus;

    [ObservableProperty] private string estateStatusChip = "not set up yet";

    public bool HasEstateStatus => !string.IsNullOrEmpty(EstateStatus);

    private string EstateLetterPath => $"{EstateFolderName}/{EstateLetterFileName}";

    private bool CanSaveEstateLetter => !IsBusy && !string.IsNullOrWhiteSpace(EstateLetterText);

    private bool CanGenerateTemplate => !IsBusy;

    /// <summary>
    /// Loads the estate letter into the editable box when Setup opens: the
    /// existing letter if one was saved before (so the owner keeps editing it),
    /// otherwise a generated starter template. Called from the constructor.
    /// </summary>
    private void LoadEstateState()
    {
        // One-time cleanup of old placeholder notes from a previous flow version.
        try
        {
            RemoveStalePlaceholders();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or VaultException)
        {
            // Non-fatal: placeholder cleanup is best-effort, but record it.
            AppLog.Handled("LoadEstateState.RemoveStalePlaceholders", e);
        }

        if (session.FileExists(EstateLetterPath))
        {
            try
            {
                EstateLetterText = Encoding.UTF8.GetString(session.ReadFile(EstateLetterPath));
                EstateStatusChip = "saved — edit and save again to update";
                return;
            }
            catch (VaultException e)
            {
                // The stored letter is unreadable (corrupt/tampered): fall through
                // to a fresh template, but do not lose that this happened.
                AppLog.Handled("LoadEstateState.ReadEstateLetter (unreadable, using fresh template)", e);
            }
        }

        EstateLetterText = BuildEstateLetter(TrimmedOwnerName(), TrimmedContact());
        EstateStatusChip = "not saved yet";
    }

    private string TrimmedOwnerName() => string.IsNullOrWhiteSpace(EstateOwnerName) ? "the archive owner" : EstateOwnerName!.Trim();

    private string TrimmedContact() => string.IsNullOrWhiteSpace(EstateContact) ? "(none provided)" : EstateContact!.Trim();

    // Regenerating the template or editing name/contact should re-enable Save.
    partial void OnEstateLetterTextChanged(string value) => SaveEstateLetterCommand.NotifyCanExecuteChanged();

    partial void OnIsBusyChanged(bool value)
    {
        SaveEstateLetterCommand.NotifyCanExecuteChanged();
        GenerateTemplateCommand.NotifyCanExecuteChanged();
        // Document CRUD-edit commands also gate on IsBusy.
        RenameDocumentCommand.NotifyCanExecuteChanged();
        MoveDocumentCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(CanReplaceDocument));
    }

    /// <summary>Replaces the editable letter with a fresh template built from name/contact.</summary>
    [RelayCommand(CanExecute = nameof(CanGenerateTemplate))]
    private void GenerateTemplate()
    {
        EstateLetterText = BuildEstateLetter(TrimmedOwnerName(), TrimmedContact());
        EstateStatus = "Template inserted. Edit it as you like, then click SAVE LETTER.";
    }

    /// <summary>Saves the (possibly edited) letter to the vault and refreshes the public readme.</summary>
    [RelayCommand(CanExecute = nameof(CanSaveEstateLetter))]
    private async Task SaveEstateLetterAsync()
    {
        string letter = EstateLetterText;
        string ownerName = TrimmedOwnerName();
        string contact = TrimmedContact();

        IsBusy = true;
        EstateStatus = "Saving your estate-planning letter…";
        try
        {
            await Task.Run(() =>
            {
                // Save the edited letter verbatim into the vault (create or overwrite).
                session.WriteFile(EstateLetterPath, Encoding.UTF8.GetBytes(letter));

                // Keep it searchable: index this one document incrementally.
                IndexEstateLetter();

                // Refresh the password-free companion on the drive's public\ folder.
                WriteEstatePublicReadme(ownerName, contact);
            });

            RefreshDashboard();
            OnDocumentsChanged();
            RecordActivity("Estate", "Estate-planning letter saved.");
            EstateStatusChip = "saved — edit and save again to update";
            EstateStatus =
                $"Saved. '{EstateFolderName}/{EstateLetterFileName}' now holds your letter, and a " +
                "password-free copy was written to the drive's public\\ folder. You can edit the text " +
                "above and click SAVE LETTER again at any time.";
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException or VaultException)
        {
            AppLog.Handled("SaveEstateLetter", e);
            EstateStatus = $"Could not save the letter: {e.Message}";
            RecordActivity("Estate", $"Estate-planning save failed: {e.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Indexes just the estate letter so it is immediately searchable.</summary>
    private void IndexEstateLetter()
    {
        VaultSearchIndex? index = getIndex();
        if (index is null)
        {
            index = VaultSearchIndex.LoadOrBuild(session);
            setIndex(index);
        }

        ArchiveManifest manifest = ManifestStore.Load(session)
            ?? ArchiveManifest.Empty(session.VaultId, "not committed");
        index.ApplyChanges(session, new SyncPlan([EstateLetterPath], [], []), manifest);
        index.Save(session);
    }

    /// <summary>Legacy placeholder filename created by an earlier estate flow.</summary>
    private const string LegacyFolderNoteFileName = "_About this folder.txt";

    /// <summary>
    /// Removes the "_About this folder.txt" placeholder documents an earlier
    /// version of this flow wrote into each recommended folder. They interfere
    /// with browse/search and are no longer created.
    /// </summary>
    private void RemoveStalePlaceholders()
    {
        foreach (string folder in RecommendedFolders)
        {
            session.RemoveFile($"{folder}/{LegacyFolderNoteFileName}");
        }
    }

    /// <summary>Writes the password-free estate explanation into &lt;driveRoot&gt;\public\.</summary>
    private void WriteEstatePublicReadme(string ownerName, string contact)
    {
        string vaultRoot = Path.GetDirectoryName(vaultPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
            ?? throw new InvalidOperationException("Unable to determine the drive root.");
        string publicDir = Path.Combine(vaultRoot, "public");
        Directory.CreateDirectory(publicDir);
        File.WriteAllText(Path.Combine(publicDir, EstatePublicFileName), BuildEstatePublicReadme(ownerName, contact), Encoding.UTF8);
    }

    private static string BuildEstateLetter(string ownerName, string contact)
    {
        var sb = new StringBuilder();
        sb.AppendLine("A LETTER TO MY FAMILY");
        sb.AppendLine("=====================");
        sb.AppendLine();
        sb.AppendLine($"If you are reading this, you have the password to {ownerName}'s emergency archive.");
        sb.AppendLine("This drive holds copies of the important documents you may need — identity");
        sb.AppendLine("papers, financial and insurance records, property and legal documents, and");
        sb.AppendLine("medical information.");
        sb.AppendLine();
        sb.AppendLine("HOW TO FIND WHAT YOU NEED");
        sb.AppendLine("  • Use the search box at the top of the window. Type what you are looking");
        sb.AppendLine("    for (for example: will, life insurance, mortgage, bank). It searches the");
        sb.AppendLine("    text inside the documents, not just their names.");
        sb.AppendLine("  • Or browse the folders on the left: Identity, Financial, Insurance,");
        sb.AppendLine("    Property, Legal, Medical, Family.");
        sb.AppendLine("  • Select a document and choose OPEN to read it, or EXPORT / COPY to save a");
        sb.AppendLine("    copy somewhere (for example to print it).");
        sb.AppendLine();
        sb.AppendLine("WHO TO CONTACT FOR HELP");
        sb.AppendLine($"  {contact}");
        sb.AppendLine();
        sb.AppendLine("WHERE THINGS ARE FILED");
        sb.AppendLine("  Identity   passports, licences, birth/marriage certificates, national ID.");
        sb.AppendLine("  Financial  bank and investment statements, account lists, tax returns.");
        sb.AppendLine("  Insurance  life, health, home, and auto policies and contacts.");
        sb.AppendLine("  Property   deeds, titles, mortgage documents, vehicle registrations.");
        sb.AppendLine("  Legal      will, trust, power of attorney, advance directive, contracts.");
        sb.AppendLine("  Medical    medical history, prescriptions, doctors' contacts.");
        sb.AppendLine("  Family     contacts, letters, and anything else meant for the family.");
        sb.AppendLine("  (Folders appear only once documents have been filed in them.)");
        sb.AppendLine();
        sb.AppendLine("IMPORTANT");
        sb.AppendLine("  • This archive is read-only. You cannot damage it by looking through it.");
        sb.AppendLine("  • Keep the drive and its password safe and separate from each other.");
        sb.AppendLine("  • If the application ever stops working, the file");
        sb.AppendLine("    public\\RECOVERY-INSTRUCTIONS.txt on this drive explains how to recover the");
        sb.AppendLine("    documents with standard open-source software.");
        sb.AppendLine();
        sb.AppendLine("(You can replace this letter at any time: it is just a document in the");
        sb.AppendLine(" 'Estate Plan' folder of your source documents.)");
        return sb.ToString();
    }

    private static string BuildEstatePublicReadme(string ownerName, string contact)
    {
        var sb = new StringBuilder();
        sb.AppendLine("ABOUT THIS DRIVE (ESTATE PLAN)");
        sb.AppendLine("==============================");
        sb.AppendLine();
        sb.AppendLine("This file can be read without any password. It contains no secrets.");
        sb.AppendLine();
        sb.AppendLine($"This USB drive is {ownerName}'s emergency document archive. It holds encrypted");
        sb.AppendLine("copies of important personal and legal documents, kept so that family members");
        sb.AppendLine("or an executor can retrieve them when needed.");
        sb.AppendLine();
        sb.AppendLine("TO OPEN THE ARCHIVE");
        sb.AppendLine("  1. Plug the drive into a Windows computer.");
        sb.AppendLine("  2. Open the drive and run START-WINDOWS.exe (in the app\\ folder).");
        sb.AppendLine("  3. Enter the archive password. If you do not have it, contact the person");
        sb.AppendLine("     below.");
        sb.AppendLine();
        sb.AppendLine("WHO TO CONTACT");
        sb.AppendLine($"  {contact}");
        sb.AppendLine();
        sb.AppendLine("IF THE APPLICATION DOES NOT WORK");
        sb.AppendLine("  Read RECOVERY-INSTRUCTIONS.txt in this same folder. It explains how a");
        sb.AppendLine("  technically competent person can recover the documents with free,");
        sb.AppendLine("  open-source software, using only the archive password.");
        sb.AppendLine();
        sb.AppendLine("PLEASE DO NOT");
        sb.AppendLine("  Delete, move, or rename any file in the vault\\ folder. Doing so can make the");
        sb.AppendLine("  documents unrecoverable.");
        return sb.ToString();
    }
}
