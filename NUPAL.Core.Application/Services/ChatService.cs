using System.Text.Json;
using System.Text.Json.Serialization;
using Nupal.Domain.Entities;
using NUPAL.Core.Application.DTOs;
using NUPAL.Core.Application.Interfaces;

namespace NUPAL.Core.Application.Services
{
    public class ChatService : IChatService
    {
        private readonly IChatConversationRepository _convoRepo;
        private readonly IChatMessageRepository _msgRepo;
        private readonly IStudentRepository _studentRepo;
        private readonly IRlRecommendationRepository _rlRepo;
        private readonly IAgentClient _agent;
        private readonly IAgentPipelineTraceRepository _traceRepo;

        public ChatService(
            IChatConversationRepository convoRepo,
            IChatMessageRepository msgRepo,
            IStudentRepository studentRepo,
            IRlRecommendationRepository rlRepo,
            IAgentClient agent,
            IAgentPipelineTraceRepository traceRepo)
        {
            _convoRepo = convoRepo;
            _msgRepo = msgRepo;
            _studentRepo = studentRepo;
            _rlRepo = rlRepo;
            _agent = agent;
            _traceRepo = traceRepo;
        }

        private static readonly JsonSerializerOptions MetadataJsonOptions = new(JsonSerializerDefaults.Web)
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        private static string ToJsonForTrace(object? value)
        {
            return JsonSerializer.Serialize(value, MetadataJsonOptions);
        }

        private static AgentPipelineTraceEvent TraceEvent(
            int order,
            string stage,
            object? data = null,
            string status = "ok",
            double? durationMs = null,
            string? error = null)
        {
            return new AgentPipelineTraceEvent
            {
                Order = order,
                Stage = stage,
                Status = status,
                At = DateTime.UtcNow,
                DurationMs = durationMs,
                DataJson = data == null ? null : ToJsonForTrace(data),
                Error = error
            };
        }

        private static JsonElement? GetRouterElement(AgentRouteResponseDto agentResp)
        {
            if (!agentResp.Router.HasValue || agentResp.Router.Value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            {
                return null;
            }

            return agentResp.Router.Value;
        }

        private static double? ExtractRouterConfidence(AgentRouteResponseDto agentResp)
        {
            var router = GetRouterElement(agentResp);
            if (!router.HasValue || router.Value.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            if (router.Value.TryGetProperty("confidence", out var confidenceProp) && confidenceProp.TryGetDouble(out var confidence))
            {
                return confidence;
            }

            return null;
        }

        private static string? ExtractRouterString(AgentRouteResponseDto agentResp, string propertyName)
        {
            var router = GetRouterElement(agentResp);
            if (!router.HasValue || router.Value.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            if (router.Value.TryGetProperty(propertyName, out var prop) && prop.ValueKind == JsonValueKind.String)
            {
                return prop.GetString();
            }

            return null;
        }

        private static string ResolveUserKind(AgentRouteResponseDto agentResp, IReadOnlyCollection<string> replyKinds)
        {
            if (!string.IsNullOrWhiteSpace(agentResp.UserKind))
            {
                return agentResp.UserKind.Trim();
            }

            if (!string.IsNullOrWhiteSpace(agentResp.Route))
            {
                return agentResp.Route switch
                {
                    "mixed_rag_rl" => "mixed",
                    "rl_only" => "rl",
                    "general_chat" => "general",
                    "unsupported" => "unsupported",
                    _ => "rag"
                };
            }

            if (replyKinds.Count == 1)
            {
                return replyKinds.First();
            }

            if (replyKinds.Count > 1)
            {
                return "mixed";
            }

            return agentResp.Intent == "recommendation" ? "rl" : agentResp.Intent == "mixed" ? "mixed" : "rag";
        }

        private static Dictionary<string, object?> BuildAgentMetadata(AgentRouteResponseDto agentResp)
        {
            var metadata = new Dictionary<string, object?>
            {
                ["source"] = "agent_deploy",
                ["trace_id"] = agentResp.TraceId,
                ["intent"] = agentResp.Intent,
                ["route"] = string.IsNullOrWhiteSpace(agentResp.Route) ? null : agentResp.Route,
                ["user_kind"] = string.IsNullOrWhiteSpace(agentResp.UserKind) ? null : agentResp.UserKind,
                ["status"] = string.IsNullOrWhiteSpace(agentResp.Status) ? null : agentResp.Status,
                ["route_confidence"] = ExtractRouterConfidence(agentResp),
                ["route_reason"] = ExtractRouterString(agentResp, "reason")
            };

            var router = GetRouterElement(agentResp);
            if (router.HasValue)
            {
                metadata["router"] = router.Value;
            }

            return metadata;
        }

        private static string? SerializeUserMetadata(AgentRouteResponseDto agentResp)
        {
            return JsonSerializer.Serialize(BuildAgentMetadata(agentResp), MetadataJsonOptions);
        }

        private static string? SerializeAssistantMetadata(AgentRouteResponseDto agentResp, object? resultMetadata)
        {
            var metadata = new Dictionary<string, object?>
            {
                ["agent"] = BuildAgentMetadata(agentResp),
                ["result"] = resultMetadata
            };

            return JsonSerializer.Serialize(metadata, MetadataJsonOptions);
        }


        private static string? DetectTargetTrack(string? message)
        {
            if (string.IsNullOrWhiteSpace(message)) return null;
            var text = message.ToLowerInvariant().Replace("-", "_");
            if (text.Contains("big data") || text.Contains("big_data") || text.Contains("bigdata")) return "big_data";
            if (text.Contains("media informatics") || text.Contains("media track") || text.Contains(" media ") || text.EndsWith(" media")) return "media";
            if (text.Contains("general track") || text.Contains(" general ") || text.EndsWith(" general")) return "general";
            return null;
        }

        private static string DetectObjectiveProfile(string? message)
        {
            if (string.IsNullOrWhiteSpace(message)) return "balanced";
            var text = message.ToLowerInvariant().Replace("-", "_");
            if (text.Contains("fastest") || text.Contains("graduate quickly") || text.Contains("quick graduation")) return "fastest_graduation";
            if (text.Contains("gpa safe") || text.Contains("gpa_safe") || text.Contains("safe gpa") || text.Contains("protect my gpa")) return "gpa_safe";
            if (text.Contains("programming heavy") || text.Contains("programming_heavy") || text.Contains("more programming")) return "programming_heavy";
            if (text.Contains("math heavy") || text.Contains("math_heavy") || text.Contains("more math")) return "math_heavy";
            return "balanced";
        }

        private static AgentRlRecommendationDto BuildAgentRlSnapshot(
            RlRecommendation rl,
            string targetTrack,
            string objectiveProfile)
        {
            var selectedTrack = NormalizeTargetTrackForChat(targetTrack);
            var selectedProfile = NormalizeObjectiveProfileForChat(objectiveProfile);

            var availableTracks = rl.Tracks?.Keys.ToList() ?? new List<string> { rl.TargetTrack };
            var availableProfiles = new List<string>();

            List<string> courses = rl.Courses ?? new List<string>();
            List<TermRecommendation>? slates = rl.SlatesByTerm;
            RecommendationMetrics? metrics = rl.Metrics;
            string? rawResponseJson = null;

            if (rl.Tracks != null && rl.Tracks.TryGetValue(selectedTrack, out var trackRecommendation))
            {
                courses = trackRecommendation.Courses ?? new List<string>();
                slates = trackRecommendation.SlatesByTerm;
                metrics = trackRecommendation.Metrics;
                availableProfiles = trackRecommendation.Profiles?.Keys.ToList() ?? new List<string>();

                if (trackRecommendation.Profiles != null &&
                    trackRecommendation.Profiles.TryGetValue(selectedProfile, out var profileRecommendation))
                {
                    courses = profileRecommendation.Courses ?? new List<string>();
                    slates = profileRecommendation.SlatesByTerm;
                    metrics = profileRecommendation.Metrics;
                }

                if (rl.RawResponsesByTrack != null &&
                    rl.RawResponsesByTrack.TryGetValue(selectedTrack, out var raw))
                {
                    rawResponseJson = raw;
                }
            }
            else
            {
                availableProfiles = rl.Profiles?.Keys.ToList() ?? new List<string>();

                if (rl.Profiles != null &&
                    rl.Profiles.TryGetValue(selectedProfile, out var profileRecommendation))
                {
                    courses = profileRecommendation.Courses ?? new List<string>();
                    slates = profileRecommendation.SlatesByTerm;
                    metrics = profileRecommendation.Metrics;
                }
            }

            return new AgentRlRecommendationDto
            {
                RecommendationId = rl.Id.ToString(),
                TermIndex = slates?.FirstOrDefault()?.Term ?? rl.TermIndex,
                Courses = courses,
                TargetTrack = selectedTrack,
                ObjectiveProfile = selectedProfile,
                AvailableTracks = availableTracks,
                AvailableProfiles = availableProfiles,
                SlatesByTerm = slates,
                Metrics = metrics,
                RawResponseJson = rawResponseJson,
                ModelVersion = rl.ModelVersion,
                PolicyVersion = rl.PolicyVersion
            };
        }

        private static string NormalizeTargetTrackForChat(string? targetTrack)
        {
            var raw = (targetTrack ?? "general").Trim().ToLowerInvariant().Replace("-", "_").Replace(" ", "_");
            return raw switch
            {
                "bigdata" or "big_data" or "big_data_track" => "big_data",
                "media" or "media_informatics" or "media_track" => "media",
                "general" or "general_track" => "general",
                _ => "general"
            };
        }

        private static string NormalizeObjectiveProfileForChat(string? profile)
        {
            if (string.IsNullOrWhiteSpace(profile)) return "balanced";
            return profile.Trim().ToLowerInvariant().Replace("-", "_").Replace(" ", "_");
        }

        public async Task<ChatSendResponseDto> SendAsync(string studentId, ChatSendRequestDto request, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(studentId))
                throw new ArgumentException("studentId is required");

            if (request == null || string.IsNullOrWhiteSpace(request.Message))
                throw new ArgumentException("message is required");

            // 1) Resolve conversation
            ChatConversation? convo = null;
            if (!string.IsNullOrWhiteSpace(request.ConversationId))
            {
                convo = await _convoRepo.GetByIdAsync(request.ConversationId);
                if (convo != null && convo.StudentId != studentId)
                {
                    // Prevent cross-user access
                    convo = null;
                }
            }

            if (convo == null)
            {
                convo = await _convoRepo.CreateAsync(new ChatConversation
                {
                    StudentId = studentId,
                    CreatedAt = DateTime.UtcNow,
                    LastActivityAt = DateTime.UtcNow
                });
            }

            var convoId = convo.Id.ToString();

            // 2) Fetch history (oldest -> newest)
            var history = await _msgRepo.GetRecentByConversationAsync(convoId, limit: 30);
            var agentHistory = history
                .OrderBy(m => m.CreatedAt)
                .Select(m => new AgentHistoryMessageDto
                {
                    Role = m.Role,
                    Kind = m.Kind,
                    Content = m.Content
                })
                .ToList();

            // Add current message for routing (not yet persisted so we can tag it correctly)
            agentHistory.Add(new AgentHistoryMessageDto
            {
                Role = "user",
                Kind = "unknown",
                Content = request.Message.Trim()
            });

            // 4) Fetch the best matching stored RL recommendation snapshot.
            // After track-aware bundling, one MongoDB recommendation may contain:
            // tracks.general, tracks.big_data, tracks.media
            // and each track may contain several objective profiles.
            AgentRlRecommendationDto? rlSnap = null;
            var requestedTargetTrack = DetectTargetTrack(request.Message);
            var requestedProfile = DetectObjectiveProfile(request.Message);
            var lookupTargetTrack = requestedTargetTrack ?? "general";

            var student = await _studentRepo.GetByIdAsync(studentId);
            if (student != null)
            {
                RlRecommendation? rl = null;

                if (!string.IsNullOrWhiteSpace(student.LatestRecommendationId))
                {
                    rl = await _rlRepo.GetByIdAsync(student.LatestRecommendationId);
                    Console.WriteLine($"[ChatService] Found RL via LatestRecommendationId: {student.LatestRecommendationId}");
                }

                rl ??= await _rlRepo.GetLatestByStudentIdAsync(studentId, "general", "balanced");
                rl ??= await _rlRepo.GetLatestByStudentIdAsync(studentId);

                if (rl != null)
                {
                    rlSnap = BuildAgentRlSnapshot(rl, lookupTargetTrack, requestedProfile);

                    Console.WriteLine(
                        $"[ChatService] Sending RL recommendation to agent: " +
                        $"Track={rlSnap.TargetTrack}, Profile={rlSnap.ObjectiveProfile}, " +
                        $"TermIndex={rlSnap.TermIndex}, Courses={rlSnap.Courses?.Count ?? 0}");
                }
                else
                {
                    rlSnap = new AgentRlRecommendationDto
                    {
                        TargetTrack = lookupTargetTrack,
                        ObjectiveProfile = requestedProfile,
                        Courses = new List<string>()
                    };

                    Console.WriteLine($"[ChatService] No RL recommendation found for student={studentId}, track={lookupTargetTrack}, profile={requestedProfile}");
                }
            }
            else
            {
                Console.WriteLine($"[ChatService] Student not found: {studentId}");
            }

            // 5) Route via agent
            var backendTraceId = Guid.NewGuid().ToString("N");
            var agentStartedAt = DateTime.UtcNow;
            var agentStopwatch = System.Diagnostics.Stopwatch.StartNew();

            var agentReq = new AgentRouteRequestDto
            {
                StudentId = studentId,
                ConversationId = convoId,
                MessageId = backendTraceId,
                Message = request.Message.Trim(),
                History = agentHistory,
                RlRecommendation = rlSnap
            };

            var traceEvents = new List<AgentPipelineTraceEvent>
            {
                TraceEvent(1, "backend.received_message", new
                {
                    student_id = studentId,
                    conversation_id = convoId,
                    request_conversation_id = request.ConversationId,
                    message = request.Message.Trim()
                }),
                TraceEvent(2, "backend.loaded_history", new
                {
                    conversation_id = convoId,
                    history_count = history.Count,
                    agent_history_count = agentHistory.Count,
                    history = agentHistory
                }),
                TraceEvent(3, "backend.loaded_rl_snapshot", new
                {
                    has_snapshot = rlSnap != null,
                    recommendation_id = rlSnap?.RecommendationId,
                    target_track = rlSnap?.TargetTrack,
                    objective_profile = rlSnap?.ObjectiveProfile,
                    term_index = rlSnap?.TermIndex,
                    courses = rlSnap?.Courses,
                    available_tracks = rlSnap?.AvailableTracks,
                    available_profiles = rlSnap?.AvailableProfiles,
                    metrics = rlSnap?.Metrics,
                    model_version = rlSnap?.ModelVersion,
                    policy_version = rlSnap?.PolicyVersion
                }),
                TraceEvent(4, "backend.agent_request", agentReq)
            };

            var agentResp = await _agent.RouteAsync(agentReq, ct);

            agentStopwatch.Stop();

            traceEvents.Add(TraceEvent(5, "backend.agent_response", new
            {
                trace_id = agentResp.TraceId,
                intent = agentResp.Intent,
                route = agentResp.Route,
                user_kind = agentResp.UserKind,
                status = agentResp.Status,
                router = agentResp.Router,
                results_count = agentResp.Results.Count,
                results = agentResp.Results
            }, durationMs: agentStopwatch.Elapsed.TotalMilliseconds));

            // 6) Persist the user message with the resolved kind and route metadata.
            // Prefer the new agent route/user_kind fields, but fall back to legacy intent/reply kinds
            // so older agent deployments still work during a rolling deploy.
            var replyKinds = agentResp.Results
                .Select(r => r.Kind)
                .Where(k => !string.IsNullOrWhiteSpace(k))
                .Distinct()
                .ToList();
            var userKind = ResolveUserKind(agentResp, replyKinds);
            var routeConfidence = ExtractRouterConfidence(agentResp);
            var routeReason = ExtractRouterString(agentResp, "reason");
            var userMetadataJson = SerializeUserMetadata(agentResp);

            var userChatMessage = new ChatMessage
            {
                ConversationId = convoId,
                StudentId = studentId,
                Role = "user",
                Kind = userKind,
                Content = request.Message.Trim(),
                MetadataJson = userMetadataJson,
                AgentTraceId = agentResp.TraceId,
                AgentIntent = agentResp.Intent,
                AgentRoute = string.IsNullOrWhiteSpace(agentResp.Route) ? null : agentResp.Route,
                AgentUserKind = string.IsNullOrWhiteSpace(agentResp.UserKind) ? userKind : agentResp.UserKind,
                AgentStatus = agentResp.Status,
                RouteConfidence = routeConfidence,
                RouteReason = routeReason,
                CreatedAt = DateTime.UtcNow
            };

            await _msgRepo.CreateAsync(userChatMessage);

            Console.WriteLine($"[ChatService] Agent route: TraceId={agentResp.TraceId ?? "n/a"}, Route={agentResp.Route ?? "n/a"}, Intent={agentResp.Intent}, Status={agentResp.Status}, Confidence={routeConfidence?.ToString("0.###") ?? "n/a"}");

            // 7) Persist assistant replies with both result-level metadata and the shared route metadata.
            var replies = new List<ChatReplyDto>();
            var assistantMessageIds = new List<string>();

            foreach (var r in agentResp.Results)
            {
                var metadataJson = SerializeAssistantMetadata(agentResp, r.Metadata);

                var assistantChatMessage = new ChatMessage
                {
                    ConversationId = convoId,
                    StudentId = studentId,
                    Role = "assistant",
                    Kind = string.IsNullOrWhiteSpace(r.Kind) ? userKind : r.Kind,
                    Content = r.Answer,
                    MetadataJson = metadataJson,
                    AgentTraceId = agentResp.TraceId,
                    AgentIntent = agentResp.Intent,
                    AgentRoute = string.IsNullOrWhiteSpace(agentResp.Route) ? null : agentResp.Route,
                    AgentUserKind = string.IsNullOrWhiteSpace(agentResp.UserKind) ? userKind : agentResp.UserKind,
                    AgentStatus = agentResp.Status,
                    RouteConfidence = routeConfidence,
                    RouteReason = routeReason,
                    CreatedAt = DateTime.UtcNow
                };

                await _msgRepo.CreateAsync(assistantChatMessage);
                assistantMessageIds.Add(assistantChatMessage.Id.ToString());

                replies.Add(new ChatReplyDto
                {
                    Kind = string.IsNullOrWhiteSpace(r.Kind) ? userKind : r.Kind,
                    Content = r.Answer,
                    MetadataJson = metadataJson,
                    AgentTraceId = agentResp.TraceId,
                    AgentRoute = string.IsNullOrWhiteSpace(agentResp.Route) ? null : agentResp.Route,
                    AgentStatus = agentResp.Status
                });
            }

            traceEvents.Add(TraceEvent(6, "backend.persisted_chat_messages", new
            {
                user_message_id = userChatMessage.Id.ToString(),
                assistant_message_ids = assistantMessageIds,
                assistant_count = assistantMessageIds.Count
            }));

            await _traceRepo.CreateAsync(new AgentPipelineTrace
            {
                TraceId = backendTraceId,
                AgentTraceId = agentResp.TraceId,
                StudentId = studentId,
                ConversationId = convoId,
                UserMessage = request.Message.Trim(),
                UserMessageId = userChatMessage.Id.ToString(),
                AssistantMessageIds = assistantMessageIds,
                Status = "completed",
                AgentRoute = string.IsNullOrWhiteSpace(agentResp.Route) ? null : agentResp.Route,
                AgentIntent = agentResp.Intent,
                AgentUserKind = string.IsNullOrWhiteSpace(agentResp.UserKind) ? userKind : agentResp.UserKind,
                AgentStatus = agentResp.Status,
                RouteConfidence = routeConfidence,
                RouteReason = routeReason,
                TotalDurationMs = (DateTime.UtcNow - agentStartedAt).TotalMilliseconds,
                AgentRequestJson = ToJsonForTrace(agentReq),
                AgentResponseJson = ToJsonForTrace(agentResp),
                Events = traceEvents,
                CreatedAt = agentStartedAt,
                CompletedAt = DateTime.UtcNow
            });

            await _convoRepo.TouchAsync(convoId);

            // Update title if it's a new conversation (or has no title) and this is the first user message
            if (string.IsNullOrEmpty(convo.Title))
            {
               // Simple strategy: use the first 50 chars of the message
               var title = request.Message.Trim();
               if (title.Length > 50) title = title.Substring(0, 50) + "...";
               convo.Title = title;
               await _convoRepo.UpdateAsync(convo); 
            }

            return new ChatSendResponseDto
            {
                ConversationId = convoId,
                Replies = replies
            };
        }

        public async Task<List<ChatConversationDto>> GetConversationsAsync(string studentId)
        {
            var convos = await _convoRepo.GetLatestByStudentAsync(studentId);
            var results = new List<ChatConversationDto>();

            foreach (var c in convos)
            {
                // If title is missing, try to resolve it from the first message
                if (string.IsNullOrEmpty(c.Title))
                {
                    // Fetch oldest message because title usually comes from start
                    // But our repo only has "GetRecent" which is sorted desc. 
                    // Let's get "Recent" limit 1? No, recent is newest.
                    // We need a message to be the title. Newer is arguably better than nothing?
                    // Let's fetch recent messages.
                    var msgs = await _msgRepo.GetRecentByConversationAsync(c.Id.ToString(), 1);
                    
                    if (msgs.Any())
                    {
                        var firstMsg = msgs.First(); 
                        // Wait, if we want the "original" request, we might want the oldest. But we don't have GetOldest.
                        // For lazy migration purposes, the Last message is fine, or any message is fine.
                        // Actually, if we just want to filter EMPTY chats, checking Any() is enough.
                        
                        // We can set a generic title or use the last message content.
                        var content = firstMsg.Content;
                        if (content.Length > 50) content = content.Substring(0, 50) + "...";
                        c.Title = content;
                        
                        // Persist it so next time we don't query
                        await _convoRepo.UpdateAsync(c);
                    }
                    else
                    {
                         // No messages found -> Empty chat -> Skip
                         continue;
                    }
                }

                results.Add(new ChatConversationDto
                {
                    Id = c.Id.ToString(),
                    Title = c.Title!,
                    LastActivityAt = c.LastActivityAt,
                    IsPinned = c.IsPinned
                });
            }
            return results;
        }

        public async Task<List<ChatMessageDto>> GetMessagesAsync(string conversationId)
        {
            var msgs = await _msgRepo.GetRecentByConversationAsync(conversationId, 100); // Fetch more for history
            return msgs.OrderBy(m => m.CreatedAt).Select(m => new ChatMessageDto
            {
                Id = m.Id.ToString(),
                Role = m.Role,
                Kind = m.Kind,
                Content = m.Content,
                MetadataJson = m.MetadataJson,
                AgentTraceId = m.AgentTraceId,
                AgentRoute = m.AgentRoute,
                AgentStatus = m.AgentStatus,
                RouteConfidence = m.RouteConfidence,
                RouteReason = m.RouteReason,
                CreatedAt = m.CreatedAt
            }).ToList();
        }

        public async Task DeleteConversationAsync(string studentId, string conversationId)
        {
            var convo = await _convoRepo.GetByIdAsync(conversationId);
            if (convo == null) return;
            
            // Validate ownership
            if (convo.StudentId != studentId) 
                throw new UnauthorizedAccessException("Cannot delete another student's conversation");

            await _convoRepo.DeleteAsync(conversationId);
            await _msgRepo.DeleteByConversationIdAsync(conversationId);
        }

        public async Task TogglePinAsync(string studentId, string conversationId, bool isPinned)
        {
            var convo = await _convoRepo.GetByIdAsync(conversationId);
            if (convo == null) return;
            
            if (convo.StudentId != studentId)
                throw new UnauthorizedAccessException();

            convo.IsPinned = isPinned;
            await _convoRepo.UpdateAsync(convo);
        }

        public async Task RenameConversationAsync(string studentId, string conversationId, string newTitle)
        {
            var convo = await _convoRepo.GetByIdAsync(conversationId);
            if (convo == null) return;
            
            if (convo.StudentId != studentId)
                throw new UnauthorizedAccessException();

            if (string.IsNullOrWhiteSpace(newTitle)) return;

            convo.Title = newTitle;
            await _convoRepo.UpdateAsync(convo);
        }
    }
}
