// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Bot.Builder;
using Microsoft.Bot.Builder.AI.Luis;
using Microsoft.Bot.Builder.Dialogs;
using Microsoft.Bot.Schema;
using Microsoft.Extensions.Logging;
using Microsoft.Recognizers.Text;
using Newtonsoft.Json;

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
        private const string WelcomeText = @"안녕하세요. 청소년 자살예방을 위한 챗봇 길그리미입니다." +
         "상담을 시작하기에 앞서 상담과정에서 필요하다면 전화 혹은 문자 연결이 있을 수도 있어요. 동의해주실수 있나요?";
        private const string ImmediateTreatmentText = @"지금바로 청소년긴급상담센터로 연결해드리겠습니다. 잠시만 기다려주세요.";
        private const string WarningText = @"혹시 지금 당장 자살 충동을 느껴서 즉각적인 도움이 필요하다면 언제든지 '도와주세요'라고 입력해주세요";
        private const string YesOrNoText = @"'네' 혹은 '아니오'로 답해주세요.";
        private const string ErrorText = @"오류가 발생했습니다. 처음부터 다시 시작해주세요.";
        private const string NoNeedEndingText = @"그렇다면 길그리미의 도움이 없어도 괜찮을 것 같아요. 자살 문제가 아니라 학업, 교우 관계, 가족 문제 등으로 상담하고 싶은 경우 아래 링크를 통해 청소년 사이버 상담 센터에서 도움을 받을 수 있어요.";
        private const string HighRiskText = @"자살 고위험군이에요. 자살에 관한 생각을 자주 하고 실제로 시도하거나 계획을 세워본 적이 있군요. 전문가들의 즉각적인 도움이 필요한 상태에요. 지금 바로 청소년긴급상담센터로 연결해줄 수 있어요.";
        private const string LowRiskText = @"자살 저위험군이에요. 가끔 자살에 관한 생각을 하지만 실제로 시도해본 적은 없고 아직 혼자 힘으로 이겨낼 여지가 충분해요. 아래 URL통한 심리검사를 통해서 자신의 우울감과 자살 생각에 대해 진단하고 자각하는 데 도움을 줄게요.";

        // The bot state accessor object. Use this to access specific state properties.
        private readonly EchoBotAccessors _accessors;

        private LuisRecognizer Recognizer { get; } = null;

        private DialogSet _dialogs;

        /// <summary>Contains the IDs for the other dialogs in the set.</summary>
        private static class Dialogs
        {
            public const string GetAgreementAsync = "getAgreementAsync";
            public const string AskFeelingAsync = "askFeelingAsync,";
            public const string ConfirmationAsync = "confirmationAsync";
            public const string SuicidalThinkingAsync = "sicidalThinkingAsync";
            public const string RelationshipAsync = "relationshipingAsync";
            public const string FrequencyofFeelingAsync = "frequencyofFeelingAsync";
            public const string TrySuicideAsync = "trySuicideAsync";
            public const string PlanSuicideAsync = "planSuicideAsync";
            public const string BeforeResultAsync = "beforeResultAsync";
            public const string ResultAsync = "resultAsync";
        }


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
            var standard_waterfallSteps = new WaterfallStep[]
            {
                GetAgreementAsync,
                AskFeelingAsync,
                SuicidalThinkingAsync,
                RelationshipAsync,
                FrequencyofFeelingAsync,
                TrySuicideAsync,
                PlanSuicideAsync,
                BeforeResultAsync,
                ResultAsync,
            };



            // Add named dialogs to the DialogSet. These names are saved in the dialog state.
            _dialogs.Add(new WaterfallDialog("details", standard_waterfallSteps));
           // _dialogs.Add(new WaterfallDialog(Dialogs.ConfirmationAsync, feelingAgain_waterfallSteps));
            _dialogs.Add(new TextPrompt("name"));
            _dialogs.Add(new TextPrompt("feeling"));
           // _dialogs.Add(new TextPrompt("confirmation"));
            _dialogs.Add(new TextPrompt("suicidalthinking"));
            _dialogs.Add(new TextPrompt("frequency"));
            _dialogs.Add(new TextPrompt("trysuicide"));
            _dialogs.Add(new TextPrompt("plansuicide"));
            _dialogs.Add(new TextPrompt("beforeresult"));


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
                    await turnContext.SendActivityAsync(ImmediateTreatmentText, cancellationToken: cancellationToken);
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
            // Save the user profile updates into the user state.
            await _accessors.UserState.SaveChangesAsync(turnContext, false, cancellationToken);
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
            await stepContext.Context.SendActivityAsync(MessageFactory.Text($"안녕하세요, {userProfile.Name}. 반가워요."), cancellationToken);

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
            // Get the IntentScore as a double
            double dblIntentScore = (topIntent != null) ? topIntent.Value.score : 0.0;

            // Only proceed with LUIS if there is an Intent
            // and the score for the intent is greater than 70
            if (strIntent == "Nfeeling" && (dblIntentScore > 0.70))
            {
                userProfile.SuicidalRisk += 1;
                await stepContext.Context.SendActivityAsync("그랬군요. 많이 힘들었겠어요.");
                // WaterfallStep always finishes with the end of the Waterfall or with another dialog; here it is a Prompt Dialog.
                return await stepContext.PromptAsync("suicidalthinking", new PromptOptions { Prompt = MessageFactory.Text("최근에 그 감정들 때문에 죽고 싶었던 적은 있었나요?") }, cancellationToken);
            }
            else if (strIntent == "Pfeeling" && (dblIntentScore > 0.70))
            {
                userProfile.SuicidalRisk -= 1;
                await stepContext.Context.SendActivityAsync("기분이 좋아보여서 다행이에요:)");
                // WaterfallStep always finishes with the end of the Waterfall or with another dialog; here it is a Prompt Dialog.
                return await stepContext.PromptAsync("suicidalthinking", new PromptOptions { Prompt = MessageFactory.Text("그래도 혹시 최근에 죽고 싶었던 적이 있었나요?") }, cancellationToken);
            }
            else
            {
                await stepContext.Context.SendActivityAsync("조금 더 정확한 감정을 표현해줄 수 있을까요?");
                return await stepContext.ReplaceDialogAsync(Dialogs.ConfirmationAsync, null, cancellationToken);
            }
        }

        /// <summary>
        /// Get Agreement for using phone call
        /// </summary>
        private async Task<DialogTurnResult> RelationshipAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            // Get the current profile object from user state.
            var userProfile = await _accessors.UserProfile.GetAsync(stepContext.Context, () => new UserProfile(), cancellationToken);
            // Check LUIS model
            var recognizerResult = await this.Recognizer.RecognizeAsync(stepContext.Context, cancellationToken).ConfigureAwait(false);
            var topIntent = recognizerResult?.GetTopScoringIntent();
            // Get the Intent as a string
            string strIntent = (topIntent != null) ? topIntent.Value.intent : "";
            // Get the IntentScore as a double
            double dblIntentScore = (topIntent != null) ? topIntent.Value.score : 0.0;

            await stepContext.Context.SendActivityAsync($"{userProfile.SuicidalRisk}");

            // Only proceed with LUIS if there is an Intent
            // and the score for the intent is greater than 70
            if (userProfile.SuicidalRisk == -1)
            {
                if (strIntent == "Panswer" && (dblIntentScore > 0.70))
                {
                    userProfile.SuicidalRisk += 2;
                }
                else if (strIntent == "Nanswer" && (dblIntentScore > 0.70))
                {
                    await stepContext.Context.SendActivityAsync(NoNeedEndingText, cancellationToken: cancellationToken);
                    // WaterfallStep always finishes with the end of the Waterfall or with another dialog, here it is the end.
                    return await stepContext.EndDialogAsync(cancellationToken: cancellationToken);
                }
                else
                {
                    await stepContext.Context.SendActivityAsync(YesOrNoText, cancellationToken: cancellationToken);
                    return await stepContext.ReplaceDialogAsync(Dialogs.RelationshipAsync, null, cancellationToken);
                }
            }
            else if (userProfile.SuicidalRisk == 1)
            {
                if (strIntent == "Panswer" && (dblIntentScore > 0.70))
                {
                    userProfile.SuicidalRisk += 3;
                }
                else if (strIntent == "Nanswer" && (dblIntentScore > 0.70))
                {
                    userProfile.SuicidalRisk += 0;
                }
                else
                {
                    await stepContext.Context.SendActivityAsync(YesOrNoText, cancellationToken: cancellationToken);
                    return await stepContext.ReplaceDialogAsync(Dialogs.RelationshipAsync, null, cancellationToken);
                }
            }
            else
            {
                await stepContext.Context.SendActivityAsync(ErrorText, cancellationToken: cancellationToken);
                // WaterfallStep always finishes with the end of the Waterfall or with another dialog, here it is the end.
                return await stepContext.EndDialogAsync(cancellationToken: cancellationToken);
            }
            return await stepContext.PromptAsync("talk", new PromptOptions { Prompt = MessageFactory.Text("죽고 싶은 생각이나 부정적인 감정에 관해 이야기 할 사람이 있나요? 있다면 누구와 이야기 해보았나요?") }, cancellationToken);
        }

        private async Task<DialogTurnResult> FrequencyofFeelingAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            // Get the current profile object from user state.
            var userProfile = await _accessors.UserProfile.GetAsync(stepContext.Context, () => new UserProfile(), cancellationToken);
            // Check LUIS model
            var recognizerResult = await this.Recognizer.RecognizeAsync(stepContext.Context, cancellationToken).ConfigureAwait(false);
            var topIntent = recognizerResult?.GetTopScoringIntent();
            // Get the Intent as a string
            string strIntent = (topIntent != null) ? topIntent.Value.intent : "";
            // Get the IntentScore as a double
            double dblIntentScore = (topIntent != null) ? topIntent.Value.score : 0.0;

            // Only proceed with LUIS if there is an Intent
            // and the score for the intent is greater than 70
            if (strIntent == "Alone" && (dblIntentScore > 0.70))
            {
                userProfile.SuicidalRisk += 2;
                await stepContext.Context.SendActivityAsync("혼자서 고민하고 있었군요. 이제 제가 도와드릴게요.");
            }
            else if (strIntent == "Pfeeling" && (dblIntentScore > 0.70))
            {
                userProfile.SuicidalRisk -= 1;
                await stepContext.Context.SendActivityAsync("타인에게 털어 놓을 수 있다는 건 좋은 현상이에요.");
            }
            else
            {
                await stepContext.Context.SendActivityAsync("구체적으로 주로 혼자 고민하나요? 아니면 가족이나 친구와 이야기 해보나요?");
                return await stepContext.ReplaceDialogAsync(Dialogs.FrequencyofFeelingAsync, null, cancellationToken);
            }
            return await stepContext.PromptAsync("frequency", new PromptOptions { Prompt = MessageFactory.Text("죽고 싶은 생각이나 부정적인 감정이 한 주에 얼마나 자주 나타났나요?") }, cancellationToken);
        }

        private async Task<DialogTurnResult> TrySuicideAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            // Get the current profile object from user state.
            var userProfile = await _accessors.UserProfile.GetAsync(stepContext.Context, () => new UserProfile(), cancellationToken);
            // Check LUIS model
            var recognizerResult = await this.Recognizer.RecognizeAsync(stepContext.Context, cancellationToken).ConfigureAwait(false);
            var topIntent = recognizerResult?.GetTopScoringIntent();       
            // Get the Intent as a string
            string strIntent = (topIntent != null) ? topIntent.Value.intent : "";
            // Get the IntentScore as a double
            double dblIntentScore = (topIntent != null) ? topIntent.Value.score : 0.0;

            // Only proceed with LUIS if there is an Intent
            // and the score for the intent is greater than 70
            if (strIntent == "Frequency" && (dblIntentScore > 0.70))
            { 
                var entity = recognizerResult?.Entities;
                var obj = Newtonsoft.Json.Linq.JObject.Parse(JsonConvert.SerializeObject(entity)).SelectToken("전혀");

                // If entities are picked up on by LUIS, store them in state.Search
                // Also, update state.Searching to "yes", so you don't ask the user
                // what they want to search for, they've already told you
                if (obj != null)
                {
                    // update state, and save save
                    userProfile.Frequency = obj.ToString().Replace("\"", "").Trim(']', '[').Trim();
                    userProfile.SuicidalRisk += 0;
                }

                obj = Newtonsoft.Json.Linq.JObject.Parse(JsonConvert.SerializeObject(entity)).SelectToken("가끔");

                // If entities are picked up on by LUIS, store them in state.Search
                // Also, update state.Searching to "yes", so you don't ask the user
                // what they want to search for, they've already told you
                if (obj != null)
                {
                    // update state, and save save
                    userProfile.Frequency = obj.ToString().Replace("\"", "").Trim(']', '[').Trim();
                    userProfile.SuicidalRisk += 1;
                }

                obj = Newtonsoft.Json.Linq.JObject.Parse(JsonConvert.SerializeObject(entity)).SelectToken("자주");

                // If entities are picked up on by LUIS, store them in state.Search
                // Also, update state.Searching to "yes", so you don't ask the user
                // what they want to search for, they've already told you
                if (obj != null)
                {
                    // update state, and save save
                    userProfile.Frequency = obj.ToString().Replace("\"", "").Trim(']', '[').Trim();
                    userProfile.SuicidalRisk += 2;
                }

               obj = Newtonsoft.Json.Linq.JObject.Parse(JsonConvert.SerializeObject(entity)).SelectToken("항상");

                // If entities are picked up on by LUIS, store them in state.Search
                // Also, update state.Searching to "yes", so you don't ask the user
                // what they want to search for, they've already told you
                if (obj != null)
                {
                    // update state, and save save
                    userProfile.Frequency = obj.ToString().Replace("\"", "").Trim(']', '[').Trim();
                    userProfile.SuicidalRisk += 3;
                }
            }
            return await stepContext.PromptAsync("trysuicide", new PromptOptions { Prompt = MessageFactory.Text("실제로 자살을 시도해본 적이 있나요?") }, cancellationToken);
        }

        private async Task<DialogTurnResult> PlanSuicideAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            // Get the current profile object from user state.
            var userProfile = await _accessors.UserProfile.GetAsync(stepContext.Context, () => new UserProfile(), cancellationToken);
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
                userProfile.SuicidalRisk += 5;
                await stepContext.Context.SendActivityAsync(ImmediateTreatmentText);
                return await stepContext.EndDialogAsync(cancellationToken: cancellationToken);
            }
            else if (strIntent == "Nanswer" && (dblIntentScore > 0.70))
            {
                return await stepContext.PromptAsync("plansuicide", new PromptOptions { Prompt = MessageFactory.Text("그렇다면 시도는 하지 않았더라도 구체적인 자살 계획은 세워본 적이 있나요?") }, cancellationToken);
            }
            else
            {
                await stepContext.Context.SendActivityAsync(YesOrNoText, cancellationToken: cancellationToken);
                return await stepContext.ReplaceDialogAsync(Dialogs.PlanSuicideAsync, null, cancellationToken);
            }
        }

        private async Task<DialogTurnResult> BeforeResultAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            // Get the current profile object from user state.
            var userProfile = await _accessors.UserProfile.GetAsync(stepContext.Context, () => new UserProfile(), cancellationToken);
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
                userProfile.SuicidalRisk += 3;
            }
            else if (strIntent == "Nanswer" && (dblIntentScore > 0.70))
            {
                userProfile.SuicidalRisk += 0;
            }
            else
            {
                await stepContext.Context.SendActivityAsync(YesOrNoText, cancellationToken: cancellationToken);
                return await stepContext.ReplaceDialogAsync(Dialogs.BeforeResultAsync, null, cancellationToken);
            }
            return await stepContext.PromptAsync("beforeresult", new PromptOptions { Prompt = MessageFactory.Text("상담 결과를 체크하는 중입니다.") }, cancellationToken);
        }

        private async Task<DialogTurnResult> ResultAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            // Get the current profile object from user state.
            var userProfile = await _accessors.UserProfile.GetAsync(stepContext.Context, () => new UserProfile(), cancellationToken);

            if (userProfile.SuicidalRisk > 5)
            {
                await stepContext.Context.SendActivityAsync(HighRiskText);
                return await stepContext.EndDialogAsync(cancellationToken: cancellationToken);
            }
            else
            {
                await stepContext.Context.SendActivityAsync(LowRiskText);
                return await stepContext.EndDialogAsync(cancellationToken: cancellationToken);
            }
        }
    }
} 