using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Azure.Sdk.Tools.Cli.Models.AiCompletion;
using Azure.Sdk.Tools.Cli.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Identity.Client;

namespace Azure.Sdk.Tools.Cli.Services
{
    /// <summary>
    /// Implementation of the AI completion service.
    /// </summary>
    public class AiCompletionService : IAiCompletionService
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger<AiCompletionService> _logger;
        private readonly AiCompletionOptions _options;
        private readonly JsonSerializerOptions _jsonOptions;
        private readonly IPublicClientApplication? _msalApp;

        public AiCompletionService(
            HttpClient httpClient,
            ILogger<AiCompletionService> logger,
            IOptions<AiCompletionOptions> options)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _options = options.Value ?? throw new ArgumentNullException(nameof(options));

            // Initialize MSAL for authentication (always required)
            if (!string.IsNullOrEmpty(_options.ClientId) && !string.IsNullOrEmpty(_options.TenantId))
            {
                var builder = PublicClientApplicationBuilder
                    .Create(_options.ClientId)
                    .WithAuthority($"https://login.microsoftonline.com/{_options.TenantId}")
                    .WithDefaultRedirectUri(); // Use MSAL's default redirect URI for public clients

                _logger.LogDebug("Initializing MSAL with ClientId: {ClientId}, TenantId: {TenantId}", 
                    _options.ClientId, _options.TenantId);

                _msalApp = builder.Build();
                
                // Configure persistent token cache
                ConfigureTokenCache(_msalApp);
            }

            _jsonOptions = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
                WriteIndented = false // Use compact JSON by default
            };

            ConfigureHttpClient();
        }

        public async Task<CompletionResponse> SendCompletionRequestAsync(
            CompletionRequest request,
            CancellationToken cancellationToken = default)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            if (!ValidateRequest(request))
            {
                throw new ArgumentException("Request validation failed", nameof(request));
            }

            try
            {
                // Use configured endpoint
                var requestUri = new Uri(new Uri(_options.Endpoint), "/completion");

                using var httpRequest = new HttpRequestMessage(HttpMethod.Post, requestUri);

                // Add authentication headers (always required)
                if (_msalApp != null)
                {
                    try
                    {
                        var scopes = _options.Scopes;
                        
                        // Try silent authentication first
                        var accounts = await _msalApp.GetAccountsAsync();
                        AuthenticationResult? result = null;

                        if (accounts.Any())
                        {
                            try
                            {
                                _logger.LogDebug("Attempting silent token acquisition from cache for {AccountCount} accounts", accounts.Count());
                                result = await _msalApp.AcquireTokenSilent(scopes, accounts.FirstOrDefault())
                                    .ExecuteAsync(cancellationToken);
                                _logger.LogInformation("Successfully acquired token from cache");
                            }
                            catch (MsalUiRequiredException ex)
                            {
                                _logger.LogDebug("Silent authentication failed, will use interactive: {Message}", ex.Message);
                            }
                        }
                        else
                        {
                            _logger.LogDebug("No cached accounts found, will use interactive authentication");
                        }

                        // If silent authentication failed, use interactive authentication
                        if (result == null)
                        {
                            _logger.LogInformation("Prompting for interactive authentication");
                            result = await _msalApp.AcquireTokenInteractive(scopes)
                                .ExecuteAsync(cancellationToken);
                            _logger.LogInformation("Interactive authentication completed successfully");
                        }

                        if (!string.IsNullOrEmpty(result.AccessToken))
                        {
                            httpRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", result.AccessToken);
                            _logger.LogDebug("Added Bearer token to request");
                        }
                        else
                        {
                            _logger.LogWarning("No access token was obtained");
                            throw new InvalidOperationException("Authentication failed: No access token obtained");
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to obtain access token for API request");
                        throw new InvalidOperationException("Authentication failed", ex);
                    }
                }
                else
                {
                    throw new InvalidOperationException("MSAL application not initialized. Check ClientId and TenantId configuration.");
                }

                httpRequest.Content = JsonContent.Create(request, options: _jsonOptions);

                _logger.LogInformation("Sending AI completion request to {Endpoint} with question length: {Length}",
                    requestUri, request.Message.Content.Length);

                var response = await _httpClient.SendAsync(httpRequest, cancellationToken)
                    .ConfigureAwait(false);

                return await HandleHttpResponse(response, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                _logger.LogWarning("AI completion request was cancelled");
                throw;
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "HTTP error calling AI completion endpoint");
                throw new InvalidOperationException($"Failed to call AI completion endpoint: {ex.Message}", ex);
            }
            catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
            {
                _logger.LogError(ex, "Request to AI completion endpoint timed out");
                throw new InvalidOperationException("Request to AI completion endpoint timed out", ex);
            }
            catch (JsonException ex)
            {
                _logger.LogError(ex, "Failed to deserialize AI completion response");
                throw new InvalidOperationException($"Invalid response format from AI completion endpoint: {ex.Message}", ex);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error calling AI completion endpoint");
                throw new InvalidOperationException($"Unexpected error calling AI completion endpoint: {ex.Message}", ex);
            }
        }

        public bool ValidateRequest(CompletionRequest request)
        {
            if (request == null)
            {
                _logger.LogWarning("Request validation failed: Request cannot be null");
                return false;
            }

            if (string.IsNullOrWhiteSpace(request.Message?.Content))
            {
                _logger.LogWarning("Request validation failed: Message content cannot be empty");
                return false;
            }

            return true;
        }

        private async Task<CompletionResponse> HandleHttpResponse(
            HttpResponseMessage response,
            CancellationToken cancellationToken)
        {
            if (response.IsSuccessStatusCode)
            {
                var responseContent = await response.Content.ReadFromJsonAsync<CompletionResponse>(
                    _jsonOptions, cancellationToken).ConfigureAwait(false);

                if (responseContent == null)
                {
                    throw new InvalidOperationException("Received null response from AI completion endpoint");
                }

                _logger.LogInformation("Received AI completion response with ID: {Id}, HasResult: {HasResult}",
                    responseContent.Id, responseContent.HasResult);

                return responseContent;
            }

            // Handle error responses
            await HandleErrorResponse(response, cancellationToken).ConfigureAwait(false);

            // This should never be reached due to exception throwing above
            throw new InvalidOperationException($"Unexpected response status: {response.StatusCode}");
        }

        private async Task HandleErrorResponse(HttpResponseMessage response, CancellationToken cancellationToken)
        {
            var content = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            
            _logger.LogError("AI completion API returned error status {StatusCode} with content: {Content}",
                response.StatusCode, content);

            throw response.StatusCode switch
            {
                HttpStatusCode.Unauthorized => new InvalidOperationException("Unauthorized: Authentication failed"),
                HttpStatusCode.BadRequest => new ArgumentException($"Bad request: {content}"),
                HttpStatusCode.TooManyRequests => new InvalidOperationException("Rate limit exceeded. Please try again later."),
                HttpStatusCode.InternalServerError => new InvalidOperationException("AI completion service is experiencing issues"),
                _ => new InvalidOperationException($"AI completion request failed with status {response.StatusCode}: {content}")
            };
        }

        private void ConfigureHttpClient()
        {
            _httpClient.Timeout = TimeSpan.FromSeconds(120); // 2 minutes timeout

            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Azure-SDK-Tools-CLI");
            _httpClient.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        }

        private void ConfigureTokenCache(IPublicClientApplication app)
        {
            // Configure in-memory token cache for the lifetime of the MCP server process
            _logger.LogDebug("Configuring in-memory token cache for MSAL");
            
            // Set up token cache callbacks with detailed logging
            app.UserTokenCache.SetBeforeAccess(notificationArgs =>
            {
                _logger.LogDebug("Token cache access - HasStateChanged: {HasStateChanged}, SuggestedCacheKey: {SuggestedCacheKey}", 
                    notificationArgs.HasStateChanged, notificationArgs.SuggestedCacheKey);
            });
            
            app.UserTokenCache.SetAfterAccess(notificationArgs =>
            {
                _logger.LogDebug("Token cache after access - HasStateChanged: {HasStateChanged}", notificationArgs.HasStateChanged);
                if (notificationArgs.HasStateChanged)
                {
                    _logger.LogInformation("Token cache has been updated with new authentication data");
                }
            });
        }
    }
}
