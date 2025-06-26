using Discord.Interactions;
using Discord;
using Microsoft.Extensions.Options;
using WarpVoice.Options;

namespace WarpVoice.Modules
{
    public class ChoiceAutocompleteHandler : AutocompleteHandler
    {
        public override async Task<AutocompletionResult> GenerateSuggestionsAsync(IInteractionContext context, IAutocompleteInteraction autocompleteInteraction, IParameterInfo parameter, IServiceProvider services)
        {
            string userInput = autocompleteInteraction.Data.Current.Value?.ToString() ?? "";
            var options = services.GetRequiredService<IOptions<AddressBookOptions>>();
            var nameNumbers = options.Value.NameNumbers;
            // You can make this dynamic — call a DB, API, etc.
            var matchingOptions = nameNumbers
                .Where(x => x.Key.StartsWith(userInput, StringComparison.OrdinalIgnoreCase))
                .Take(5) // Discord limits to 25 suggestions max
                .Select(kvp => new AutocompleteResult(kvp.Key, kvp.Value));

            return AutocompletionResult.FromSuccess(matchingOptions);
        }
    }
}
