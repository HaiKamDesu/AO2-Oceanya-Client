using System;
using System.Threading.Tasks;
using System.Windows;
using AOBot_Testing.Structures;
using Common;

namespace OceanyaClient
{
    /// <summary>
    /// Provides a single workflow for rebuilding client-side character and background indexes.
    /// </summary>
    public static class ClientAssetRefreshService
    {
        /// <summary>
        /// Refreshes character and background caches while showing progress in <see cref="WaitForm"/>.
        /// </summary>
        /// <param name="owner">Owning window for the progress dialog.</param>
        public static async Task RefreshCharactersAndBackgroundsAsync(Window owner)
        {
            await WaitForm.ShowFormAsync("Refreshing character and background info...", owner);

            try
            {
                Globals.UpdateConfigINI(Globals.PathToConfigINI);

                CharacterFolder.RefreshCharacterList(
                    onParsedCharacter: (CharacterFolder ini) =>
                    {
                        WaitForm.SetSubtitle("Parsed Character: " + ini.Name);
                    },
                    onChangedMountPath: (string path) =>
                    {
                        WaitForm.SetSubtitle("Changed mount path: " + path);
                    });

                foreach (CharacterFolder character in CharacterFolder.FullList)
                {
                    WaitForm.SetSubtitle("Integrity verify: " + character.Name);
                    _ = CharacterIntegrityVerifier.RunAndPersist(character);
                }

                Background.RefreshCache(
                    onChangedMountPath: (string path) =>
                    {
                        WaitForm.SetSubtitle("Indexed background mount path: " + path);
                    });
            }
            finally
            {
                WaitForm.CloseForm();
            }
        }
    }
}
