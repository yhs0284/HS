﻿// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ChatbotHS.Dialogs;
using Microsoft.Bot.Builder;
using Microsoft.Bot.Builder.AI.Luis;
using Microsoft.Bot.Builder.Dialogs;
using Microsoft.Bot.Schema;
using Microsoft.Extensions.Logging;
using Microsoft.Recognizers.Text;

namespace ChatbotHS
{
    /// <summary>
    /// Represents a bot that processes incoming activities.
    /// For each user interaction, an instance of this class is created and the OnTurnAsync method is called.
    /// This is a Transient lifetime service.  Transient lifetime services are created
    /// each time they're requested. For each Activity received, a new instance of this
    /// class is created. Objects that are expensive to construct, or have a lifetime
    /// beyond the single turn, should be carefully managed.
    /// For example, the <see cref="MemoryStorage"/> object and associated
    /// <see cref="IStatePropertyAccessor{T}"/> object are created with a singleton lifetime.
    /// </summary>
    /// <seealso cref="https://docs.microsoft.com/en-us/aspnet/core/fundamentals/dependency-injection?view=aspnetcore-2.1"/>
    public class EchoWithCounterBot : IBot
    {
        private const string WelcomeText = @"안녕하세요. 청소년 자살예방을 위한 챗봇 길그리미입니다.\r\n" +
         "상담을 시작하기에 앞서 상담과정에서 필요하다면 전화 혹은 문자 연결이 있을 수도 있어요. 동의해주실수 있나요?";

        private const string WarningText = @"혹시 지금 당장 자살 충동을 느껴서 즉각적인 도움이 필요하다면 언제든지 '도와주세요'라고 입력해주세요";

        // The bot state accessor object. Use this to access specific state properties.
        private readonly EchoBotAccessors _accessors;

        private LuisRecognizer Recognizer { get; } = null;

        private DialogSet _dialogs;

        /// <summary>
        /// Initializes a new instance of the <see cref="EchoWithCounterBot"/> class.
        /// </summary>
        /// <param name="accessors">A class containing <see cref="IStatePropertyAccessor{T}"/> used to manage state.</param>
        /// <param name="loggerFactory">A <see cref="ILoggerFactory"/> that is hooked to the Azure App Service provider.</param>
        /// <seealso cref="https://docs.microsoft.com/en-us/aspnet/core/fundamentals/logging/?view=aspnetcore-2.1#windows-eventlog-provider"/>
        public EchoWithCounterBot(EchoBotAccessors accessors, LuisRecognizer luis)
        {
            _accessors = accessors ?? throw new System.ArgumentNullException("accessor can't be null");

            // DialogState accessor
            _dialogs = new DialogSet(accessors.ConversationDialogState);

            // This array defines how the Waterfall will execute.
            var waterfallSteps = new WaterfallStep[]
            {
                GetAgreementAsync,
                AskFeelingAsync,
            };

            // Add named dialogs to the DialogSet. These names are saved in the dialog state.
            _dialogs.Add(new WaterfallDialog("details", waterfallSteps));
            _dialogs.Add(new TextPrompt("name"));
            _dialogs.Add(new TextPrompt("feeling"));

            // The incoming luis variable is the LUIS Recognizer we added above.
            this.Recognizer = luis ?? throw new System.ArgumentNullException(nameof(luis));

            /*
            // Rather than explicitly coding a Waterfall we have only to declare what properties we want collected.
            // In this example we will want two text prompts to run, one for the first name and one for the last.
            var fullname_slots = new List<SlotDetails>
            {
                new SlotDetails("first", "text", "Please enter your first name."),
                new SlotDetails("last", "text", "Please enter your last name."),
            };

            // This defines an address dialog that collects street, city and zip properties.
            var address_slots = new List<SlotDetails>
            {
                new SlotDetails("street", "text", "Please enter the street."),
                new SlotDetails("city", "text", "Please enter the city."),
                new SlotDetails("zip", "text", "Please enter the zip."),
            };

            // Dialogs can be nested and the slot filling dialog makes use of that. In this example some of the child
            // dialogs are slot filling dialogs themselves.
            var slots = new List<SlotDetails>
            {
                new SlotDetails("fullname", "fullname"),
                new SlotDetails("age", "number", "Please enter your age."),
                new SlotDetails("shoesize", "shoesize", "Please enter your shoe size.", "You must enter a size between 0 and 16. Half sizes are acceptable."),
                new SlotDetails("address", "address"),
            };

            // Add the various dialogs that will be used to the DialogSet.
            _dialogs.Add(new SlotFillingDialog("address", address_slots));
            _dialogs.Add(new SlotFillingDialog("fullname", fullname_slots));
            _dialogs.Add(new TextPrompt("text"));
            _dialogs.Add(new NumberPrompt<int>("number", defaultLocale: Culture.English));
            _dialogs.Add(new NumberPrompt<float>("shoesize", ShoeSizeAsync, defaultLocale: Culture.English));
            _dialogs.Add(new SlotFillingDialog("slot-dialog", slots));

            // Defines a simple two step Waterfall to test the slot dialog.
            _dialogs.Add(new WaterfallDialog("root", new WaterfallStep[] { StartDialogAsync, ProcessResultsAsync }));
            */

        }

        /// <summary>
        /// Every conversation turn for our Echo Bot will call this method.
        /// There are no dialogs used, since it's "single turn" processing, meaning a single
        /// request and response.
        /// </summary>
        /// <param name="turnContext">A <see cref="ITurnContext"/> containing all the data needed
        /// for processing this conversation turn. </param>
        /// <param name="cancellationToken">(Optional) A <see cref="CancellationToken"/> that can be used by other objects
        /// or threads to receive notice of cancellation.</param>
        /// <returns>A <see cref="Task"/> that represents the work queued to execute.</returns>
        /// <seealso cref="BotStateSet"/>
        /// <seealso cref="ConversationState"/>
        /// <seealso cref="IMiddleware"/>
        public async Task OnTurnAsync(ITurnContext turnContext, CancellationToken cancellationToken = default(CancellationToken))
        {
            // Handle Message activity type, which is the main activity type for shown within a conversational interface
            // Message activities may contain text, speech, interactive cards, and binary or unknown attachments.
            // see https://aka.ms/about-bot-activity-message to learn more about the message and other activity types
            if (turnContext.Activity.Type == ActivityTypes.Message)
            {
                // Check LUIS model
                var recognizerResult = await this.Recognizer.RecognizeAsync(turnContext, cancellationToken);
                var topIntent = recognizerResult?.GetTopScoringIntent();
                // Get the Intent as a string
                string strIntent = (topIntent != null) ? topIntent.Value.intent : "";
                // Get the IntentScore as a double
                double dblIntentScore = (topIntent != null) ? topIntent.Value.score : 0.0;

                // Only proceed with LUIS if there is an Intent
                // and the score for the intent is greater than 70
                if (strIntent == "Priority_Danger" && (dblIntentScore > 0.80))
                {
                    await turnContext.SendActivityAsync($"지금바로 청소년긴급상담센터로 연결해드리겠습니다. 잠시만 기다려주세요.", cancellationToken: cancellationToken);
                    // 전화연결!!!!!
                }
                // Run the DialogSet - let the framework identify the current state of the dialog from
                // the dialog stack and figure out what (if any) is the active dialog.
                var dialogContext = await _dialogs.CreateContextAsync(turnContext, cancellationToken);
                var results = await dialogContext.ContinueDialogAsync(cancellationToken);

                // If the DialogTurnStatus is Empty we should start a new dialog.
                if (results.Status == DialogTurnStatus.Empty)
                {
                    await dialogContext.BeginDialogAsync("details", null, cancellationToken);
                }
            }

            // Processes ConversationUpdate Activities to welcome the user.
            else if (turnContext.Activity.Type == ActivityTypes.ConversationUpdate)
            {
                if (turnContext.Activity.MembersAdded != null)
                {
                    await SendWelcomeMessageAsync(turnContext, cancellationToken);
                }
            }
            else
            {
                await turnContext.SendActivityAsync($"{turnContext.Activity.Type} activity detected", cancellationToken: cancellationToken);
            }

            // Save the dialog state into the conversation state.
            await _accessors.ConversationState.SaveChangesAsync(turnContext, false, cancellationToken);
        }



        /// <summary>
        /// Sends a welcome message to the user.
        /// </summary>
        /// <param name="turnContext">A <see cref="ITurnContext"/> containing all the data needed
        /// for processing this conversation turn. </param>
        /// <param name="cancellationToken">A <see cref="CancellationToken"/> that can be used by other objects
        /// or threads to receive notice of cancellation.</param>
        /// <returns>A <see cref="Task"/> that represents the work queued to execute.</returns>
        private static async Task SendWelcomeMessageAsync(ITurnContext turnContext, CancellationToken cancellationToken)
        {
            foreach (var member in turnContext.Activity.MembersAdded)
            {
                if (member.Id != turnContext.Activity.Recipient.Id)
                {
                    var reply = turnContext.Activity.CreateReply();
                    reply.Text = WelcomeText;
                    await turnContext.SendActivityAsync(reply, cancellationToken);
                }
            }
        }


        /// <summary>
        /// Get Agreement for using phone call
        /// </summary>
        private async Task<DialogTurnResult> GetAgreementAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            // Check LUIS model
            var recognizerResult = await this.Recognizer.RecognizeAsync(stepContext.Context, cancellationToken).ConfigureAwait(false);
            var topIntent = recognizerResult?.GetTopScoringIntent();
            // Get the Intent as a string
            string strIntent = (topIntent != null) ? topIntent.Value.intent : "";
            // Get the IntentScore as a double
            double dblIntentScore = (topIntent != null) ? topIntent.Value.score : 0.0;

            // Only proceed with LUIS if there is an Intent
            // and the score for the intent is greater than 70
            if (strIntent == "Panswer" && (dblIntentScore > 0.70))
            {
                await stepContext.Context.SendActivityAsync("고마워요. 그럼 이제부터 상담을 진행할게요.", cancellationToken: cancellationToken);
                await stepContext.Context.SendActivityAsync(WarningText, cancellationToken: cancellationToken);
                // WaterfallStep always finishes with the end of the Waterfall or with another dialog; here it is a Prompt Dialog.
                // Running a prompt here means the next WaterfallStep will be run when the users response is received.
                return await stepContext.PromptAsync("name", new PromptOptions { Prompt = MessageFactory.Text("친구의이름은 뭔가요?") }, cancellationToken);
            }
            else
            {
                await stepContext.Context.SendActivityAsync("동의해주시지 않으면 더 이상 상담 진행이 어려워요:(" + "다시 시작해주세요.", cancellationToken: cancellationToken);
                // WaterfallStep always finishes with the end of the Waterfall or with another dialog, here it is the end.
                return await stepContext.EndDialogAsync(cancellationToken: cancellationToken);
            }
        }

        /// <summary>
        /// One of the functions that make up the <see cref="WaterfallDialog"/>.
        /// </summary>
        /// <param name="stepContext">The <see cref="WaterfallStepContext"/> gives access to the executing dialog runtime.</param>
        /// <param name="cancellationToken">A <see cref="CancellationToken"/>.</param>
        /// <returns>A <see cref="DialogTurnResult"/> to communicate some flow control back to the containing WaterfallDialog.</returns>
        private async Task<DialogTurnResult> AskFeelingAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            // Get the current profile object from user state.
            var userProfile = await _accessors.UserProfile.GetAsync(stepContext.Context, () => new UserProfile(), cancellationToken);

            // Update the profile.
            userProfile.Name = (string)stepContext.Result;

            // We can send messages to the user at any point in the WaterfallStep.
            await stepContext.Context.SendActivityAsync(MessageFactory.Text($"안녕하세요, {stepContext.Result}. 반가워요."), cancellationToken);

            // WaterfallStep always finishes with the end of the Waterfall or with another dialog; here it is a Prompt Dialog.
            return await stepContext.PromptAsync("feeling", new PromptOptions { Prompt = MessageFactory.Text("요즘 기분은 어때요?") }, cancellationToken);
        }

        /// <summary>
        /// Get Agreement for using phone call
        /// </summary>
        private async Task<DialogTurnResult> SuicidalThinkingAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            // Get the current profile object from user state.
            var userProfile = await _accessors.UserProfile.GetAsync(stepContext.Context, () => new UserProfile(), cancellationToken);
            // Check LUIS model
            var recognizerResult = await this.Recognizer.RecognizeAsync(stepContext.Context, cancellationToken).ConfigureAwait(false);
            var topIntent = recognizerResult?.GetTopScoringIntent();
            // Get the Intent as a string
            string strIntent = (topIntent != null) ? topIntent.Value.intent : "";
            // Update the profile
            userProfile.FeelingIntent = strIntent;
            // Get the IntentScore as a double
            double dblIntentScore = (topIntent != null) ? topIntent.Value.score : 0.0;

            // Only proceed with LUIS if there is an Intent
            // and the score for the intent is greater than 70
            if (strIntent == "Nfeeling" && (dblIntentScore > 0.70))
            {
                await stepContext.Context.SendActivityAsync("그랬군요. 많이 힘들었겠어요.");
                // WaterfallStep always finishes with the end of the Waterfall or with another dialog; here it is a Prompt Dialog.
                return await stepContext.PromptAsync("suicidalthinking", new PromptOptions { Prompt = MessageFactory.Text("최근에 그 감정들 때문에 죽고 싶었던 적은 없었나요?") }, cancellationToken);
            }
            else if (strIntent == "Pfeeling" && (dblIntentScore > 0.70))
            {
                await stepContext.Context.SendActivityAsync("기분이 좋아보여서 다행이에요:)");
                // WaterfallStep always finishes with the end of the Waterfall or with another dialog; here it is a Prompt Dialog.
                return await stepContext.PromptAsync("suicidalthinking", new PromptOptions { Prompt = MessageFactory.Text("그래도 혹시 최근에 죽고 싶었던 적은 없었나요?") }, cancellationToken);
            }
            else
            {
                return await stepContext.ReplaceDialogAsync(, null, cancellationToken);
            }
        }



        /*
        /// <summary>
        /// This is an example of a custom validator. This example can be directly used on a float NumberPrompt.
        /// Returning true indicates the recognized value is acceptable. Returning false will trigger re-prompt behavior.
        /// </summary>
        /// <param name="promptContext">The <see cref="PromptValidatorContext"/> giv     s the validator code access to the runtime, including the recognized value and the turn context.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>A an asynchronous Task of bool indicating validation success as true.</returns>
        private Task<bool> ShoeSizeAsync(PromptValidatorContext<float> promptContext, CancellationToken cancellationToken)
        {
            var shoesize = promptContext.Recognized.Value;

            // show sizes can range from 0 to 16
            if (shoesize >= 0 && shoesize <= 16)
            {
                // we only accept round numbers or half sizes
                if (Math.Floor(shoesize) == shoesize || Math.Floor(shoesize * 2) == shoesize * 2)
                {
                    // indicate success by returning the value
                    return Task.FromResult(true);
                }
            }

            return Task.FromResult(false);
        }

        /// <summary>
        /// One of the functions that make up the <see cref="WaterfallDialog"/>.
        /// </summary>
        /// <param name="stepContext">The <see cref="WaterfallStepContext"/> gives access to the executing dialog runtime.</param>
        /// <param name="cancellationToken">A <see cref="CancellationToken"/>.</param>
        /// <returns>A <see cref="DialogTurnResult"/> to communicate some flow control back to the containing WaterfallDialog.</returns>
        private async Task<DialogTurnResult> StartDialogAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            // Start the child dialog. This will run the top slot dialog than will complete when all the properties are gathered.
            return await stepContext.BeginDialogAsync("slot-dialog", null, cancellationToken);
        }

        /// <summary>
        /// One of the functions that make up the <see cref="WaterfallDialog"/>.
        /// </summary>
        /// <param name="stepContext">The <see cref="WaterfallStepContext"/> gives access to the executing dialog runtime.</param>
        /// <param name="cancellationToken">A <see cref="CancellationToken"/>.</param>
        /// <returns>A <see cref="DialogTurnResult"/> to communicate some flow control back to the containing WaterfallDialog.</returns>
        private async Task<DialogTurnResult> ProcessResultsAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            // To demonstrate that the slot dialog collected all the properties we will echo them back to the user.
            if (stepContext.Result is IDictionary<string, object> result && result.Count > 0)
            {
                var fullname = (IDictionary<string, object>)result["fullname"];
                await stepContext.Context.SendActivityAsync(MessageFactory.Text($"{fullname["first"]} {fullname["last"]}"), cancellationToken);

                await stepContext.Context.SendActivityAsync(MessageFactory.Text($"{result["shoesize"]}"), cancellationToken);

                var address = (IDictionary<string, object>)result["address"];
                await stepContext.Context.SendActivityAsync(MessageFactory.Text($"{address["street"]} {address["city"]} {address["zip"]}"), cancellationToken);
            }

            // Remember to call EndAsync to indicate to the runtime that this is the end of our waterfall.
            return await stepContext.EndDialogAsync();
        }
        */
    }
}
