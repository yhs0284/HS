// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System.Threading;
using System.Threading.Tasks;
using Microsoft.Bot.Builder;
using Microsoft.Bot.Builder.AI.Luis;
using Microsoft.Bot.Builder.Dialogs;
using Microsoft.Bot.Schema;
using Microsoft.Extensions.Logging;

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
        private LuisRecognizer Recognizer { get; } = null;
        private DialogSet _dialogs;

        private const string WelcomeMessage = @"안녕하세요. 청소년 자살예방을 위한 챗봇 길그리미입니다.";
        private const string InfoMessage = @"상담을 시작하기에 앞서 상담과정에서 필요하다면 전화 혹은 문자 연결이 있을 수도 있어요. 동의해주실수 있나요?";
        private const string PatternMessage = @"혹시 지금 당장 자살 충동을 느껴서 즉각적인 도움이 필요하다면 언제든지 '도와주세요'라고 입력해주세요";

        // The bot state accessor object. Use this to access specific state properties.
        private readonly EchoBotAccessors _accessors;

        // Services configured from the ".bot" file.
        // private readonly BotServices _services;

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
            // This array defines how the Waterfall will execute
            var waterfallSteps = new WaterfallStep[]
           {
                NameStepAsync,
                NameConfirmStepAsync,
                AgeStepAsync,
                ConfirmStepAsync,
                SummaryStepAsync,
           };
            // Add named dialogs to the DialogSet. These names are saved in the dialog state.
            _dialogs.Add(new WaterfallDialog("details", waterfallSteps));
            _dialogs.Add(new TextPrompt("name"));
            _dialogs.Add(new NumberPrompt<int>("age"));
            _dialogs.Add(new ConfirmPrompt("confirm"));

            // The incoming luis variable is the LUIS Recognizer we added above.
            this.Recognizer = luis ?? throw new System.ArgumentNullException(nameof(luis));
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

            // use state accessor to extract the didBotWelcomeUser flag
            var didBotWelcomeUser = await _accessors.CounterState.GetAsync(turnContext, () => new CounterState());

            // 유저가 대답하기 전 먼저 환영 문구와 봇 관련 정보를 출력한다.
            if (didBotWelcomeUser.DidBotWelcomeUser == false)
            {
                didBotWelcomeUser.DidBotWelcomeUser = true;

                // Update user state flag to reflect bot handled first user interaction.
                await _accessors.CounterState.SetAsync(turnContext, didBotWelcomeUser);
                await _accessors.ConversationState.SaveChangesAsync(turnContext);

                await turnContext.SendActivityAsync(WelcomeMessage, cancellationToken: cancellationToken);
                await turnContext.SendActivityAsync(PatternMessage, cancellationToken: cancellationToken);
                await turnContext.SendActivityAsync(InfoMessage, cancellationToken: cancellationToken);

            }

            if (turnContext == null)
            {
                throw new System.ArgumentNullException(nameof(turnContext));
            }

            // Handle Message activity type, which is the main activity type for shown within a conversational interface
            // Message activities may contain text, speech, interactive cards, and binary or unknown attachments.
            // see https://aka.ms/about-bot-activity-message to learn more about the message and other activity types
            if (turnContext.Activity.Type == ActivityTypes.Message)
            {
                // use state accessor to extract the didBotWelcomeUser flag
                var didBotGetAgreement = await _accessors.CounterState.GetAsync(turnContext, () => new CounterState());

                // 유저가 대답하기 전 먼저 환영 문구와 봇 관련 정보를 출력한다.
                if (didBotGetAgreement.DidBotGetAgreement == false)
                {
                    // Check LUIS model
                    var recognizerResult = await this.Recognizer.RecognizeAsync(turnContext, cancellationToken);
                    var topIntent = recognizerResult?.GetTopScoringIntent();

                    // Get the IntentScore as a string
                    string strIntent = (topIntent != null) ? topIntent.Value.intent : "";
                    // Get the IntentScore as a double
                    double dblIntentScore = (topIntent != null) ? topIntent.Value.score : 0.0;

                    // Only proceed with LUIS if there is an Intent
                    // and the score for the intent is greater than 70
                    if (strIntent == "Panswer" && (dblIntentScore > 0.70))
                    {
                        didBotGetAgreement.DidBotGetAgreement = true;
                        await turnContext.SendActivityAsync("고마워요. 그럼 이제부터 상담을 진행할게요.");
                        await turnContext.SendActivityAsync(PatternMessage, cancellationToken: cancellationToken);
                    }
                    else
                    {
                        await turnContext.SendActivityAsync("동의해주시지 않으면 더 이상 상담 진행이 어려워요:(" +
                            "다시 시작해주세요.");
                    }
                }
                else
                {
                    // Get the conversationstate from the turn context
                    var state = await _accessors.CounterState.GetAsync(turnContext, () => new CounterState());
                    // Get the user state from the turn context.
                    var user = await _accessors.UserProfile.GetAsync(turnContext, () => new UserProfile());
                    if (user.Name == null)
                    {
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
                    // Set the property using the accessor.
                    await _accessors.CounterState.SetAsync(turnContext, state);

                    // Save the new turn count into the conversation state.
                    await _accessors.ConversationState.SaveChangesAsync(turnContext);
                    // Save the user profile updates into the user state.
                    await _accessors.UserState.SaveChangesAsync(turnContext, false, cancellationToken);
                }
            }
        }


 

        private async Task<DialogTurnResult> NameStepAsync(
            WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            // Running a prompt here means the next WaterfallStep will be 
            // run when the users response is received.
            return await stepContext.PromptAsync(
                "name", new PromptOptions { Prompt = MessageFactory.Text("친구의 이름은 뭔가요?") }
                , cancellationToken);
        }

        /// <summary>
        /// One of the functions that make up the <see cref="WaterfallDialog"/>.
        /// </summary>
        /// <param name="stepContext">The <see cref="WaterfallStepContext"/> gives access to the executing dialog runtime.</param>
        /// <param name="cancellationToken">A <see cref="CancellationToken"/>.</param>
        /// <returns>A <see cref="DialogTurnResult"/> to communicate some flow control back to the containing WaterfallDialog.</returns>
        private async Task<DialogTurnResult> NameConfirmStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            // Get the current profile object from user state.
            var userProfile = await _accessors.UserProfile.GetAsync(stepContext.Context, () => new UserProfile(), cancellationToken);

            // Update the profile.
            userProfile.Name = (string)stepContext.Result;

            // We can send messages to the user at any point in the WaterfallStep.
            await stepContext.Context.SendActivityAsync(MessageFactory.Text($"Thanks {stepContext.Result}."), cancellationToken);

            // WaterfallStep always finishes with the end of the Waterfall or with another dialog; here it is a Prompt Dialog.
            return await stepContext.PromptAsync("confirm", new PromptOptions { Prompt = MessageFactory.Text("Would you like to give your age?") }, cancellationToken);
        }

        /// <summary>
        /// One of the functions that make up the <see cref="WaterfallDialog"/>.
        /// </summary>
        /// <param name="stepContext">The <see cref="WaterfallStepContext"/> gives access to the executing dialog runtime.</param>
        /// <param name="cancellationToken">A <see cref="CancellationToken"/>.</param>
        /// <returns>A <see cref="DialogTurnResult"/> to communicate some flow control back to the containing WaterfallDialog.</returns>
        private async Task<DialogTurnResult> AgeStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            if ((bool)stepContext.Result)
            {
                // User said "yes" so we will be prompting for the age.

                // Get the current profile object from user state.
                var userProfile = await _accessors.UserProfile.GetAsync(stepContext.Context, () => new UserProfile(), cancellationToken);

                // WaterfallStep always finishes with the end of the Waterfall or with another dialog, here it is a Prompt Dialog.
                return await stepContext.PromptAsync("age", new PromptOptions { Prompt = MessageFactory.Text("Please enter your age.") }, cancellationToken);
            }
            else
            {
                // User said "no" so we will skip the next step. Give -1 as the age.
                return await stepContext.NextAsync(-1, cancellationToken);
            }
        }

        /// <summary>
        /// One of the functions that make up the <see cref="WaterfallDialog"/>.
        /// </summary>
        /// <param name="stepContext">The <see cref="WaterfallStepContext"/> gives access to the executing dialog runtime.</param>
        /// <param name="cancellationToken">A <see cref="CancellationToken"/>.</param>
        /// <returns>A <see cref="DialogTurnResult"/> to communicate some flow control back to the containing WaterfallDialog.</returns>
        private async Task<DialogTurnResult> ConfirmStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            // Get the current profile object from user state.
            var userProfile = await _accessors.UserProfile.GetAsync(stepContext.Context, () => new UserProfile(), cancellationToken);

            // Update the profile.
            userProfile.Age = (int)stepContext.Result;

            // We can send messages to the user at any point in the WaterfallStep.
            if (userProfile.Age == -1)
            {
                await stepContext.Context.SendActivityAsync(MessageFactory.Text($"No age given."), cancellationToken);
            }
            else
            {
                // We can send messages to the user at any point in the WaterfallStep.
                await stepContext.Context.SendActivityAsync(MessageFactory.Text($"I have your age as {userProfile.Age}."), cancellationToken);
            }

            // WaterfallStep always finishes with the end of the Waterfall or with another dialog, here it is a Prompt Dialog.
            return await stepContext.PromptAsync("confirm", new PromptOptions { Prompt = MessageFactory.Text("Is this ok?") }, cancellationToken);
        }

        /// <summary>
        /// One of the functions that make up the <see cref="WaterfallDialog"/>.
        /// </summary>
        /// <param name="stepContext">The <see cref="WaterfallStepContext"/> gives access to the executing dialog runtime.</param>
        /// <param name="cancellationToken">A <see cref="CancellationToken"/>.</param>
        /// <returns>A <see cref="DialogTurnResult"/> to communicate some flow control back to the containing WaterfallDialog.</returns>
        private async Task<DialogTurnResult> SummaryStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            if ((bool)stepContext.Result)
            {
                // Get the current profile object from user state.
                var userProfile = await _accessors.UserProfile.GetAsync(stepContext.Context, () => new UserProfile(), cancellationToken);

                // We can send messages to the user at any point in the WaterfallStep.
                if (userProfile.Age == -1)
                {
                    await stepContext.Context.SendActivityAsync(MessageFactory.Text($"I have your name as {userProfile.Name}."), cancellationToken);
                }
                else
                {
                    await stepContext.Context.SendActivityAsync(MessageFactory.Text($"I have your name as {userProfile.Name} and age as {userProfile.Age}."), cancellationToken);
                }
            }
            else
            {
                // We can send messages to the user at any point in the WaterfallStep.
                await stepContext.Context.SendActivityAsync(MessageFactory.Text("Thanks. Your profile will not be kept."), cancellationToken);
            }

            // WaterfallStep always finishes with the end of the Waterfall or with another dialog, here it is the end.
            return await stepContext.EndDialogAsync(cancellationToken: cancellationToken);
        }
    }
}
