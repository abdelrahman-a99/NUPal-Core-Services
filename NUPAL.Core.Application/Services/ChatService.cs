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

        public ChatService(
            IChatConversationRepository convoRepo,
            IChatMessageRepository msgRepo,
            IStudentRepository studentRepo,
            IRlRecommendationRepository rlRepo,
            IAgentClient agent)
        {
            _convoRepo = convoRepo;
            _msgRepo = msgRepo;
            _studentRepo = studentRepo;
            _rlRepo = rlRepo;
            _agent = agent;
        }

        private static readonly JsonSerializerOptions MetadataJsonOptions = new(JsonSerializerDefaults.Web)
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

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

            // 4) Fetch latest RL recommendation snapshot (if present)
            AgentRlRecommendationDto? rlSnap = null;
            var student = await _studentRepo.GetByIdAsync(studentId);
            if (student != null)
            {
                RlRecommendation? rl = null;
                if (!string.IsNullOrWhiteSpace(student.LatestRecommendationId))
                {
                    rl = await _rlRepo.GetByIdAsync(student.LatestRecommendationId);
                    Console.WriteLine($"[ChatService] Found RL via LatestRecommendationId: {student.LatestRecommendationId}");
                }
                rl ??= await _rlRepo.GetLatestByStudentIdAsync(studentId);

                if (rl != null)
                {
                    rlSnap = new AgentRlRecommendationDto
                    {
                        TermIndex = rl.TermIndex,
                        Courses = rl.Courses ?? new List<string>(),
                        SlatesByTerm = rl.SlatesByTerm,
                        Metrics = rl.Metrics,
                        ModelVersion = rl.ModelVersion,
                        PolicyVersion = rl.PolicyVersion
                    };
                    Console.WriteLine($"[ChatService] Sending RL recommendation to agent: TermIndex={rl.TermIndex}, Courses={rl.Courses?.Count ?? 0}");
                }
                else
                {
                    Console.WriteLine($"[ChatService] No RL recommendation found for student: {studentId}");
                }
            }
            else
            {
                Console.WriteLine($"[ChatService] Student not found: {studentId}");
            }

            // 5) Route via agent
            var agentReq = new AgentRouteRequestDto
            {
                StudentId = studentId,
                Message = request.Message.Trim(),
                History = agentHistory,
                RlRecommendation = rlSnap
            };

            var agentResp = await _agent.RouteAsync(agentReq, ct);

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

            await _msgRepo.CreateAsync(new ChatMessage
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
            });

            Console.WriteLine($"[ChatService] Agent route: TraceId={agentResp.TraceId ?? "n/a"}, Route={agentResp.Route ?? "n/a"}, Intent={agentResp.Intent}, Status={agentResp.Status}, Confidence={routeConfidence?.ToString("0.###") ?? "n/a"}");

            // 7) Persist assistant replies with both result-level metadata and the shared route metadata.
            var replies = new List<ChatReplyDto>();
            foreach (var r in agentResp.Results)
            {
                var metadataJson = SerializeAssistantMetadata(agentResp, r.Metadata);

                await _msgRepo.CreateAsync(new ChatMessage
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
                });

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
