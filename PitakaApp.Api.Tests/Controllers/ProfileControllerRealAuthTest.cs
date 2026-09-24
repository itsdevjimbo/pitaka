using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Bogus;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PitakaApp.Api.Controllers;
using PitakaApp.Api.Data;
using PitakaApp.Api.Tests.Factories;
using PitakaApp.Api.Tests.Fixtures;

namespace PitakaApp.Api.Tests.Controllers;

// The Profile endpoints driven the way the spec asks: through the real JwtBearerHandler
// (RealAuthWebApplicationFactory) and the recording email sender, so a session genuinely
// survives the pending period. Asserts only what a person or client can see — status
// codes, which addresses sign in, what landed in the outbox — never token internals or
// how the pending state is stored. The one race redemption cannot show over HTTP (an
// address taken while the link sat unread) is at the data layer in
// RedeemEmailChangeConcurrencyTest.
//
// The GET api/profile block below is also the suite's only coverage of real token
// signature/issuer/audience/expiry validation — those tests moved here with the read
// when GET api/auth/me was removed (profile-self-service ticket 07). The email-change
// sections that follow are numbered against their own spec, so a ticket number here only
// means something next to the feature it names.
[Collection("RealAuthDatabase collection")]
public class ProfileControllerRealAuthTest : IDisposable
{
    private readonly Faker _faker = new();
    private readonly IServiceScope _scope;
    private readonly PitakaDbContext _context;
    private readonly HttpClient _client;
    private readonly RecordingEmailSender _emailSender;
    private readonly InMemoryFileStorage _fileStorage;

    public ProfileControllerRealAuthTest(RealAuthWebApplicationFactory factory)
    {
        _scope = factory.Services.CreateScope();
        _context = _scope.ServiceProvider.GetRequiredService<PitakaDbContext>();
        _client = factory.CreateClient();
        _emailSender = factory.EmailSender;
        _fileStorage = factory.FileStorage;
    }

    // ─── Profile self-service ticket 07: the read, moved from GET api/auth/me ───────

    [Fact]
    public async Task GetProfile_WithRealJwtFromLogin_ReturnsOk()
    {
        var email = _faker.Internet.Email();
        await UserFactory.CreateAsync(_context, email);

        var loginResponse = await _client.PostAsJsonAsync(
            "/api/auth/login",
            new { email, password = UserFactory.DefaultPassword }
        );
        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);

        var loginBody = await loginResponse.Content.ReadFromJsonAsync<LoginResponse>();
        Assert.NotNull(loginBody);

        var request = new HttpRequestMessage(HttpMethod.Get, "/api/profile");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", loginBody!.Token);

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<ProfileResponse>();
        Assert.Equal(email, body!.Email);
    }

    [Fact]
    public async Task GetProfile_WithRealJwtFromRegister_ReturnsOk()
    {
        // S2: register no longer hands back a token. This proves the whole arc with a
        // real JwtBearerHandler instead — register, read the confirmation link out of
        // the delivered mail, confirm, log in, then a real bearer token reaches the read.
        var email = _faker.Internet.Email();

        var registerResponse = await _client.PostAsJsonAsync(
            "/api/auth/register",
            new
            {
                name = _faker.Person.FullName,
                email,
                password = "TestPass123!",
            }
        );
        Assert.Equal(HttpStatusCode.Created, registerResponse.StatusCode);

        var registerBody = await registerResponse.Content.ReadFromJsonAsync<ProfileResponse>();
        Assert.NotNull(registerBody);
        Assert.False(registerBody.HasPicture);

        var confirmMessage = Assert.Single(_emailSender.To(email));
        var match = Regex.Match(
            confirmMessage.TextBody,
            @"userId=(?<userId>\d+)&token=(?<token>[^\s]+)"
        );
        Assert.True(
            match.Success,
            $"No confirm-email link found in email body:\n{confirmMessage.TextBody}"
        );

        var confirmResponse = await _client.PostAsJsonAsync(
            "/api/auth/confirm-email",
            new
            {
                userId = int.Parse(match.Groups["userId"].Value),
                token = Uri.UnescapeDataString(match.Groups["token"].Value),
            }
        );
        Assert.Equal(HttpStatusCode.NoContent, confirmResponse.StatusCode);

        var loginResponse = await _client.PostAsJsonAsync(
            "/api/auth/login",
            new { email, password = "TestPass123!" }
        );
        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);

        var loginBody = await loginResponse.Content.ReadFromJsonAsync<LoginResponse>();
        Assert.NotNull(loginBody);
        Assert.False(loginBody.User.HasPicture);

        var request = new HttpRequestMessage(HttpMethod.Get, "/api/profile");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", loginBody!.Token);

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<ProfileResponse>();
        Assert.Equal(email, body!.Email);
    }

    [Fact]
    public async Task PictureRoutes_WithRealTokens_OnlyReadTheSignedInProfilesPicture()
    {
        var firstEmail = _faker.Internet.Email();
        var secondEmail = _faker.Internet.Email();
        await UserFactory.CreateAsync(_context, firstEmail);
        await UserFactory.CreateAsync(_context, secondEmail);

        var firstLogin = await LogIn(firstEmail, UserFactory.DefaultPassword);
        var secondLogin = await LogIn(secondEmail, UserFactory.DefaultPassword);
        var firstLoginBody = (await firstLogin.Content.ReadFromJsonAsync<LoginResponse>())!;
        var secondLoginBody = (await secondLogin.Content.ReadFromJsonAsync<LoginResponse>())!;
        Assert.False(firstLoginBody.User.HasPicture);

        var upload = await PutPictureAsync(
            firstLoginBody.Token,
            ProfilePictureTestImages.CreatePng()
        );
        Assert.Equal(HttpStatusCode.NoContent, upload.StatusCode);
        var currentKey = await _context
            .Users.AsNoTracking()
            .Where(user => user.Email == firstEmail)
            .Select(user => user.Photo!.ObjectKey)
            .SingleAsync();
        Assert.True(_fileStorage.Contains(currentKey!));

        var firstPicture = await Send(
            HttpMethod.Get,
            "/api/profile/picture",
            firstLoginBody.Token,
            body: null
        );
        Assert.Equal(HttpStatusCode.OK, firstPicture.StatusCode);
        Assert.Equal("image/png", firstPicture.Content.Headers.ContentType!.MediaType);

        var secondPicture = await Send(
            HttpMethod.Get,
            "/api/profile/picture",
            secondLoginBody.Token,
            body: null
        );
        Assert.Equal(HttpStatusCode.NotFound, secondPicture.StatusCode);

        var profileRead = await Send(
            HttpMethod.Get,
            "/api/profile",
            firstLoginBody.Token,
            body: null
        );
        Assert.True((await profileRead.Content.ReadFromJsonAsync<ProfileResponse>())!.HasPicture);

        var profileUpdate = await Send(
            HttpMethod.Put,
            "/api/profile",
            firstLoginBody.Token,
            new { name = _faker.Person.FullName }
        );
        Assert.True((await profileUpdate.Content.ReadFromJsonAsync<ProfileResponse>())!.HasPicture);

        var laterLogin = await LogIn(firstEmail, UserFactory.DefaultPassword);
        Assert.True((await laterLogin.Content.ReadFromJsonAsync<LoginResponse>())!.User.HasPicture);
    }

    [Fact]
    public async Task PictureRoutes_WithoutARealBearerToken_ReturnUnauthorized()
    {
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await _client.GetAsync("/api/profile/picture")).StatusCode
        );

        using var request = new HttpRequestMessage(HttpMethod.Put, "/api/profile/picture");
        using var form = new MultipartFormDataContent();
        form.Add(new ByteArrayContent(ProfilePictureTestImages.CreatePng()), "File", "source.png");
        request.Content = form;
        Assert.Equal(HttpStatusCode.Unauthorized, (await _client.SendAsync(request)).StatusCode);

        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await _client.DeleteAsync("/api/profile/picture")).StatusCode
        );
    }

    [Fact]
    public async Task GetProfile_WithTamperedToken_ReturnsUnauthorized()
    {
        var email = _faker.Internet.Email();
        await UserFactory.CreateAsync(_context, email);

        var loginResponse = await _client.PostAsJsonAsync(
            "/api/auth/login",
            new { email, password = UserFactory.DefaultPassword }
        );
        var loginBody = await loginResponse.Content.ReadFromJsonAsync<LoginResponse>();

        // Flip the last character of the signature segment — same claims, invalid signature.
        var tamperedToken = loginBody!.Token[..^1] + (loginBody.Token[^1] == 'A' ? 'B' : 'A');

        var request = new HttpRequestMessage(HttpMethod.Get, "/api/profile");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tamperedToken);

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetProfile_WithNoToken_ReturnsUnauthorized()
    {
        var response = await _client.GetAsync("/api/profile");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ─── Email-change flow ─────────────────────────────────────────────────────────

    [Fact]
    public async Task RequestEmailChange_WithCurrentPasswordAndFreshAddress_SendsLinkAndLeavesLiveAddressWorking()
    {
        const string password = "TestPass123!";
        var oldEmail = _faker.Internet.Email();
        var newEmail = _faker.Internet.Email();
        var token = await RegisterConfirmAndLogInAsync(oldEmail, password);

        var response = await Send(
            HttpMethod.Post,
            "/api/profile/email-change",
            token,
            new { newEmail, currentPassword = password }
        );

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        // A confirmation link reached the new address, and the copy says Profile —
        // never User, never Account (CONTEXT.md).
        var message = Assert.Single(_emailSender.To(newEmail));
        Assert.Contains("Profile", message.TextBody);
        Assert.DoesNotContain("User", message.TextBody);
        Assert.DoesNotContain("Account", message.TextBody);
        Assert.DoesNotContain("User", message.Subject);
        Assert.DoesNotContain("Account", message.Subject);
        Assert.Matches(@"userId=\d+&token=\S+", message.TextBody);

        // The old address is told, alongside the confirmation to the new one (ticket 07):
        // its messages are the sign-up confirmation from registration plus a courtesy
        // notice naming the requested address and carrying no link of any kind.
        var toOld = _emailSender.To(oldEmail);
        Assert.Equal(2, toOld.Count);
        Assert.Equal("Confirm your Pitaka Profile", toOld[0].Subject);
        var notice = toOld[1];
        Assert.Contains(newEmail, notice.TextBody);
        Assert.DoesNotContain("http", notice.TextBody, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Profile", notice.TextBody);
        Assert.DoesNotContain("User", notice.TextBody);
        Assert.DoesNotContain("Account", notice.TextBody);
        Assert.DoesNotContain("User", notice.Subject);
        Assert.DoesNotContain("Account", notice.Subject);

        // The live address still signs in; the pending one does not.
        Assert.Equal(HttpStatusCode.OK, (await LogIn(oldEmail, password)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await LogIn(newEmail, password)).StatusCode);
    }

    [Fact]
    public async Task RequestEmailChange_WithWrongCurrentPassword_IsRefusedAndSendsNothing()
    {
        const string password = "TestPass123!";
        var oldEmail = _faker.Internet.Email();
        var newEmail = _faker.Internet.Email();
        var token = await RegisterConfirmAndLogInAsync(oldEmail, password);

        var response = await Send(
            HttpMethod.Post,
            "/api/profile/email-change",
            token,
            new { newEmail, currentPassword = "not-the-password" }
        );

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(_emailSender.To(newEmail));
        // A refused request sends nothing to either address — the old address still has
        // only its sign-up confirmation, no courtesy notice (ticket 07).
        var toOld = Assert.Single(_emailSender.To(oldEmail));
        Assert.Equal("Confirm your Pitaka Profile", toOld.Subject);
        Assert.Equal(HttpStatusCode.OK, (await LogIn(oldEmail, password)).StatusCode);
    }

    [Fact]
    public async Task RequestEmailChange_WithoutBearerToken_ReturnsUnauthorized()
    {
        var newEmail = _faker.Internet.Email();

        var response = await _client.PostAsJsonAsync(
            "/api/profile/email-change",
            new { newEmail, currentPassword = "TestPass123!" }
        );

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(_emailSender.To(newEmail));
    }

    [Fact]
    public async Task RequestEmailChange_ToTheProfilesOwnCurrentAddress_IsRefusedAndSendsNothing()
    {
        const string password = "TestPass123!";
        var email = _faker.Internet.Email();
        var token = await RegisterConfirmAndLogInAsync(email, password);

        var response = await Send(
            HttpMethod.Post,
            "/api/profile/email-change",
            token,
            new { newEmail = email, currentPassword = password }
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        // Only the sign-up confirmation — no "confirm your new email" was sent.
        var toSelf = Assert.Single(_emailSender.To(email));
        Assert.Equal("Confirm your Pitaka Profile", toSelf.Subject);
    }

    [Fact]
    public async Task RequestEmailChange_ToAddressHeldByAnotherProfile_ReturnsConflictAndStoresNoPendingChange()
    {
        const string password = "TestPass123!";
        var oldEmail = _faker.Internet.Email();
        var token = await RegisterConfirmAndLogInAsync(oldEmail, password);

        var otherProfile = await UserFactory.CreateAsync(_context);

        var response = await Send(
            HttpMethod.Post,
            "/api/profile/email-change",
            token,
            new { newEmail = otherProfile.Email, currentPassword = password }
        );

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Empty(_emailSender.To(otherProfile.Email!));
        // A refused request tells no one — the old address gets no courtesy notice
        // either (ticket 07).
        var toOld = Assert.Single(_emailSender.To(oldEmail));
        Assert.Equal("Confirm your Pitaka Profile", toOld.Subject);

        // The message names the remedy — pick a different address — rather than just
        // reporting the collision (ticket 06).
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.Contains("different address", problem!.Detail, StringComparison.OrdinalIgnoreCase);

        // Nothing was stored: the Profile has no pending change and can ask again.
        var profile = await ReadProfile(token);
        Assert.Null(profile.PendingEmail);
    }

    // ─── Email-change ticket 07: the old address gets told ─────────────────────────

    [Fact]
    public async Task RequestEmailChange_TellsTheOldAddress_WithANoticeNamingTheRequestedAddressAndNoLink()
    {
        const string password = "TestPass123!";
        var oldEmail = _faker.Internet.Email();
        var newEmail = _faker.Internet.Email();
        var token = await RegisterConfirmAndLogInAsync(oldEmail, password);

        // Everything the old address has received so far (the sign-up confirmation), so
        // the assertion below is about what this one request adds.
        var oldBefore = _emailSender.To(oldEmail).Count;

        var response = await Send(
            HttpMethod.Post,
            "/api/profile/email-change",
            token,
            new { newEmail, currentPassword = password }
        );
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        // One request, two sends: the confirmation link to the new address and a notice
        // to the old one.
        var toNew = Assert.Single(_emailSender.To(newEmail));
        Assert.Matches(@"userId=\d+&token=\S+", toNew.TextBody);

        Assert.Equal(oldBefore + 1, _emailSender.To(oldEmail).Count);
        var notice = _emailSender.To(oldEmail).Last();

        // It names the address that was asked for...
        Assert.Contains(newEmail, notice.TextBody);
        // ...and carries no link of any kind — it is a notice, not an undo control.
        Assert.DoesNotContain("http", notice.TextBody, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("?userId=", notice.TextBody);
        Assert.DoesNotContain("token=", notice.TextBody);

        // Says Profile, never User and never Account (CONTEXT.md).
        Assert.Contains("Profile", notice.TextBody);
        Assert.DoesNotContain("User", notice.TextBody);
        Assert.DoesNotContain("Account", notice.TextBody);
        Assert.DoesNotContain("User", notice.Subject);
        Assert.DoesNotContain("Account", notice.Subject);
    }

    // ─── Ticket 03: redeeming the link moves the address ────────────────────────────

    [Fact]
    public async Task ConfirmEmailChange_TheArc_NewAddressSignsIn_OldDoesNot_PasswordAndSessionSurvive()
    {
        const string password = "TestPass123!";
        var oldEmail = _faker.Internet.Email();
        var newEmail = _faker.Internet.Email();
        var bearer = await RegisterConfirmAndLogInAsync(oldEmail, password);

        var (userId, token) = await RequestChangeAndReadLink(bearer, newEmail, password);

        var confirm = await _client.PostAsJsonAsync(
            "/api/profile/email-change/confirm",
            new { userId, token }
        );
        Assert.Equal(HttpStatusCode.NoContent, confirm.StatusCode);

        // The new address signs in; the old one no longer does.
        Assert.Equal(HttpStatusCode.OK, (await LogIn(newEmail, password)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await LogIn(oldEmail, password)).StatusCode);

        // The password is untouched by the move — the new address signs in with the
        // same one, and nothing about it was reset.
        var withNewAddress = await LogIn(newEmail, password);
        Assert.Equal(HttpStatusCode.OK, withNewAddress.StatusCode);

        // The session made before the change still works after it — the change rotates
        // the security stamp but does not revoke issued JWTs (ADR 0011 B1/B2), and
        // GET api/profile now reports the new address.
        var read = await Send(HttpMethod.Get, "/api/profile", bearer, body: null);
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        var profile = await read.Content.ReadFromJsonAsync<ProfileResponse>();
        Assert.Equal(newEmail, profile!.Email);
    }

    [Fact]
    public async Task ConfirmEmailChange_WithGarbageToken_ReturnsOneProblemDetails400()
    {
        const string password = "TestPass123!";
        var oldEmail = _faker.Internet.Email();
        var newEmail = _faker.Internet.Email();
        var bearer = await RegisterConfirmAndLogInAsync(oldEmail, password);

        var (userId, _) = await RequestChangeAndReadLink(bearer, newEmail, password);

        var response = await _client.PostAsJsonAsync(
            "/api/profile/email-change/confirm",
            new { userId, token = "this-token-was-never-issued" }
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        // The live address is untouched — a bad token changes nothing.
        Assert.Equal(HttpStatusCode.OK, (await LogIn(oldEmail, password)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await LogIn(newEmail, password)).StatusCode);
    }

    [Fact]
    public async Task ConfirmEmailChange_WithExpiredLink_IsRefusedAndTheProfileIsUntouched()
    {
        const string password = "TestPass123!";
        var oldEmail = _faker.Internet.Email();
        var newEmail = _faker.Internet.Email();
        var bearer = await RegisterConfirmAndLogInAsync(oldEmail, password);

        var (userId, token) = await RequestChangeAndReadLink(bearer, newEmail, password);

        // Simulate the pending window having elapsed: push the stored expiry into the
        // past. The stored expiry and the token lifespan are the one value, so a link
        // whose window has passed is exactly this state.
        var pending = await _context.Users.SingleAsync(u => u.Id == userId);
        pending.PendingEmailExpiresAt = DateTime.UtcNow.AddMinutes(-1);
        await _context.SaveChangesAsync();

        var response = await _client.PostAsJsonAsync(
            "/api/profile/email-change/confirm",
            new { userId, token }
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await LogIn(oldEmail, password)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await LogIn(newEmail, password)).StatusCode);
    }

    // ─── Ticket 05: asking again replaces the pending change ────────────────────────
    //
    // The mechanism this ticket turns on — redemption checking the token's address
    // against the *stored* pending value rather than trusting the token's own payload —
    // landed with ticket 03: RedeemEmailChange verifies the token against the stored
    // PendingEmail, never a request field (spec decision 18). RequestEmailChange has
    // always overwritten both pending columns in place. No production change here; this
    // case drives a person correcting a typo (spec stories 6 and 7) and holds the
    // replace to what a person can observe.

    [Fact]
    public async Task RequestEmailChange_AskedAgainWithACorrectedAddress_ReplacesThePendingChangeAndKillsTheFirstLink()
    {
        const string password = "TestPass123!";
        var oldEmail = _faker.Internet.Email();
        var typoTarget = _faker.Internet.Email();
        var correctedTarget = _faker.Internet.Email();
        var bearer = await RegisterConfirmAndLogInAsync(oldEmail, password);

        var (userId, firstToken) = await RequestChangeAndReadLink(bearer, typoTarget, password);
        var (_, secondToken) = await RequestChangeAndReadLink(bearer, correctedTarget, password);

        // The second request replaced the pending address in place: the Profile shows
        // the corrected address and only that one — asking again replaces, a Profile
        // never holds more than one pending address (CONTEXT.md, spec story 8).
        Assert.Equal(correctedTarget, (await ReadProfile(bearer)).PendingEmail);

        // The first link is bound to typoTarget, but the stored pending address is now
        // correctedTarget — the first token no longer matches and is refused (spec
        // story 7). It fails as the same 400 with problem+json that
        // ConfirmEmailChange_WithGarbageToken_ReturnsOneProblemDetails400 pins for any
        // other dead link; the spec forbids asserting on the body past that.
        var stale = await _client.PostAsJsonAsync(
            "/api/profile/email-change/confirm",
            new { userId, token = firstToken }
        );
        Assert.Equal(HttpStatusCode.BadRequest, stale.StatusCode);
        Assert.Equal("application/problem+json", stale.Content.Headers.ContentType?.MediaType);
        Assert.Equal(HttpStatusCode.Unauthorized, (await LogIn(typoTarget, password)).StatusCode);

        // The corrected link redeems normally.
        var fresh = await _client.PostAsJsonAsync(
            "/api/profile/email-change/confirm",
            new { userId, token = secondToken }
        );
        Assert.Equal(HttpStatusCode.NoContent, fresh.StatusCode);

        // Only the address most recently asked for was applied: the corrected address
        // signs in, and neither the old address nor the typo ever can.
        Assert.Equal(HttpStatusCode.OK, (await LogIn(correctedTarget, password)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await LogIn(oldEmail, password)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await LogIn(typoTarget, password)).StatusCode);
    }

    // ─── Ticket 06: the address was claimed while the link sat unread ───────────────

    [Fact]
    public async Task ConfirmEmailChange_WhenTheAddressWasClaimedSinceTheRequest_ReturnsConflictAndLeavesTheProfileUntouched()
    {
        const string password = "TestPass123!";
        var oldEmail = _faker.Internet.Email();
        var newEmail = _faker.Internet.Email();
        var bearer = await RegisterConfirmAndLogInAsync(oldEmail, password);

        var (userId, token) = await RequestChangeAndReadLink(bearer, newEmail, password);

        // Between the request and the click, another Profile registers the same address.
        // This is the deterministic case of spec story 13 — the pool-hidden race itself
        // is covered by RedeemEmailChangeConcurrencyTest.
        await UserFactory.CreateAsync(_context, newEmail);

        var response = await _client.PostAsJsonAsync(
            "/api/profile/email-change/confirm",
            new { userId, token }
        );

        // Its own 409 — distinct from the non-specific 400 every other redemption
        // failure collapses to — and worded to say what happened (the address was
        // taken since the request) and where to go next (ask again, different address),
        // so the person does not just retry the dead link (spec story 13).
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.Contains("taken", problem!.Detail, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("different address", problem.Detail, StringComparison.OrdinalIgnoreCase);
        // Says Profile, never User or Account (CONTEXT.md).
        Assert.DoesNotContain("account", problem.Detail, StringComparison.OrdinalIgnoreCase);

        // The Profile is untouched: the live address still signs in, and the pending
        // change is still there — so the person can go straight to requesting another.
        Assert.Equal(HttpStatusCode.OK, (await LogIn(oldEmail, password)).StatusCode);
        var profile = await ReadProfile(bearer);
        Assert.Equal(oldEmail, profile.Email);
        Assert.Equal(newEmail, profile.PendingEmail);

        // And a fresh request to a different address is accepted.
        var retry = await Send(
            HttpMethod.Post,
            "/api/profile/email-change",
            bearer,
            new { newEmail = _faker.Internet.Email(), currentPassword = password }
        );
        Assert.Equal(HttpStatusCode.NoContent, retry.StatusCode);
    }

    // ─── Ticket 04: seeing and clearing a pending change ────────────────────────────

    [Fact]
    public async Task Me_WithNoPendingChange_LeavesPendingEmailNull()
    {
        const string password = "TestPass123!";
        var email = _faker.Internet.Email();
        var bearer = await RegisterConfirmAndLogInAsync(email, password);

        var profile = await ReadProfile(bearer);

        Assert.Equal(email, profile.Email);
        Assert.Null(profile.PendingEmail);
    }

    [Fact]
    public async Task Me_WithAChangeInFlight_ReportsThePendingAddressWithoutMovingTheLiveOne()
    {
        const string password = "TestPass123!";
        var oldEmail = _faker.Internet.Email();
        var newEmail = _faker.Internet.Email();
        var bearer = await RegisterConfirmAndLogInAsync(oldEmail, password);

        await RequestChangeAndReadLink(bearer, newEmail, password);

        var profile = await ReadProfile(bearer);

        // The person can see which address the pending change is going to (story 8),
        // while the live address is untouched — it still signs in, the pending one does
        // not.
        Assert.Equal(oldEmail, profile.Email);
        Assert.Equal(newEmail, profile.PendingEmail);
        Assert.Equal(HttpStatusCode.OK, (await LogIn(oldEmail, password)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await LogIn(newEmail, password)).StatusCode);
    }

    [Fact]
    public async Task Me_AfterThePendingChangeExpires_LeavesPendingEmailNull()
    {
        const string password = "TestPass123!";
        var oldEmail = _faker.Internet.Email();
        var newEmail = _faker.Internet.Email();
        var bearer = await RegisterConfirmAndLogInAsync(oldEmail, password);

        var (userId, _) = await RequestChangeAndReadLink(bearer, newEmail, password);

        // Push the stored expiry into the past — the same arrange-only style the
        // expired-link test uses. Past its expiry the pending change is absent
        // everywhere, including the Profile read, with no cleanup job.
        var pending = await _context.Users.SingleAsync(u => u.Id == userId);
        pending.PendingEmailExpiresAt = DateTime.UtcNow.AddMinutes(-1);
        await _context.SaveChangesAsync();

        var profile = await ReadProfile(bearer);

        Assert.Null(profile.PendingEmail);
    }

    [Fact]
    public async Task RequestEmailChange_AfterAnEarlierChangeExpired_ReplacesItWithoutACancel()
    {
        const string password = "TestPass123!";
        var oldEmail = _faker.Internet.Email();
        var staleTarget = _faker.Internet.Email();
        var freshTarget = _faker.Internet.Email();
        var bearer = await RegisterConfirmAndLogInAsync(oldEmail, password);

        var (userId, _) = await RequestChangeAndReadLink(bearer, staleTarget, password);

        // Let the first change lapse, then ask again — an expired pending change does not
        // block a fresh request, it is simply overwritten in place.
        var pending = await _context.Users.SingleAsync(u => u.Id == userId);
        pending.PendingEmailExpiresAt = DateTime.UtcNow.AddMinutes(-1);
        await _context.SaveChangesAsync();

        await RequestChangeAndReadLink(bearer, freshTarget, password);

        Assert.Equal(freshTarget, (await ReadProfile(bearer)).PendingEmail);
    }

    [Fact]
    public async Task CancelEmailChange_ClearsThePendingAddressAndKillsTheLink()
    {
        const string password = "TestPass123!";
        var oldEmail = _faker.Internet.Email();
        var newEmail = _faker.Internet.Email();
        var bearer = await RegisterConfirmAndLogInAsync(oldEmail, password);

        var (userId, token) = await RequestChangeAndReadLink(bearer, newEmail, password);

        var cancel = await Send(
            HttpMethod.Post,
            "/api/profile/email-change/cancel",
            bearer,
            body: null
        );
        Assert.Equal(HttpStatusCode.NoContent, cancel.StatusCode);

        // Gone from the Profile, and the link that was mailed no longer redeems — it
        // fails with the same non-specific 400 as any other dead link.
        Assert.Null((await ReadProfile(bearer)).PendingEmail);

        var redeem = await _client.PostAsJsonAsync(
            "/api/profile/email-change/confirm",
            new { userId, token }
        );
        Assert.Equal(HttpStatusCode.BadRequest, redeem.StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await LogIn(oldEmail, password)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await LogIn(newEmail, password)).StatusCode);
    }

    [Fact]
    public async Task CancelEmailChange_WithNothingPending_Succeeds()
    {
        const string password = "TestPass123!";
        var email = _faker.Internet.Email();
        var bearer = await RegisterConfirmAndLogInAsync(email, password);

        var cancel = await Send(
            HttpMethod.Post,
            "/api/profile/email-change/cancel",
            bearer,
            body: null
        );

        Assert.Equal(HttpStatusCode.NoContent, cancel.StatusCode);
    }

    [Fact]
    public async Task CancelEmailChange_WithoutBearerToken_ReturnsUnauthorized()
    {
        var response = await _client.PostAsync("/api/profile/email-change/cancel", content: null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ─── Profile self-service ticket 08: changing your name ────────────────────────

    [Fact]
    public async Task UpdateProfile_ChangesTheNameForReal_ItSurvivesAFreshLogin()
    {
        const string password = "TestPass123!";
        var email = _faker.Internet.Email();
        var bearer = await RegisterConfirmAndLogInAsync(email, password);

        var newName = _faker.Person.FullName;
        var response = await Send(HttpMethod.Put, "/api/profile", bearer, new { name = newName });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<ProfileResponse>();
        Assert.Equal(newName, body!.Name);
        Assert.Equal(email, body.Email);

        // The change is a real persisted mutation, not a transform of one response: a
        // brand-new session — fresh JWT, fresh read — sees the new name too (spec story
        // 10, "real and not cosmetic").
        var freshLogin = await LogIn(email, password);
        var freshBearer = (await freshLogin.Content.ReadFromJsonAsync<LoginResponse>())!.Token;
        Assert.Equal(newName, (await ReadProfile(freshBearer)).Name);
    }

    [Fact]
    public async Task UpdateProfile_TakesNoPasswordGate_AndLeavesTheCallersSessionWorking()
    {
        const string password = "TestPass123!";
        var email = _faker.Internet.Email();
        var bearer = await RegisterConfirmAndLogInAsync(email, password);

        // No current password is supplied — the request has no field for one — and the
        // rename still succeeds.
        var response = await Send(
            HttpMethod.Put,
            "/api/profile",
            bearer,
            new { name = _faker.Person.FullName }
        );
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // The session that made the change still works after it, and still signs in.
        Assert.Equal(
            HttpStatusCode.OK,
            (await Send(HttpMethod.Get, "/api/profile", bearer, body: null)).StatusCode
        );
        Assert.Equal(HttpStatusCode.OK, (await LogIn(email, password)).StatusCode);
    }

    [Fact]
    public async Task UpdateProfile_WithoutBearerToken_ReturnsUnauthorized()
    {
        var response = await _client.PutAsJsonAsync(
            "/api/profile",
            new { name = _faker.Person.FullName }
        );
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ─── Profile self-service ticket 09: changing your password while signed in ─────

    [Fact]
    public async Task ChangePassword_WithTheCurrentPasswordAndAValidNewOne_Is204_AndSwapsWhichPasswordSignsIn()
    {
        const string oldPassword = "TestPass123!";
        const string newPassword = "A-Fresh-Passphrase-9";
        var email = _faker.Internet.Email();
        var bearer = await RegisterConfirmAndLogInAsync(email, oldPassword);

        var response = await Send(
            HttpMethod.Post,
            "/api/profile/password",
            bearer,
            new { oldPassword, newPassword }
        );

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsByteArrayAsync());

        // The new password signs in afterwards and the old one no longer does.
        Assert.Equal(HttpStatusCode.OK, (await LogIn(email, newPassword)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await LogIn(email, oldPassword)).StatusCode);
    }

    [Fact]
    public async Task ChangePassword_LeavesTheCallersOwnSessionWorking()
    {
        const string oldPassword = "TestPass123!";
        const string newPassword = "A-Fresh-Passphrase-9";
        var email = _faker.Internet.Email();
        var bearer = await RegisterConfirmAndLogInAsync(email, oldPassword);

        var response = await Send(
            HttpMethod.Post,
            "/api/profile/password",
            bearer,
            new { oldPassword, newPassword }
        );
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        // The change rotates the security stamp but does not revoke issued JWTs
        // (ADR 0011) — the session that made the change still reads the Profile.
        var read = await Send(HttpMethod.Get, "/api/profile", bearer, body: null);
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
    }

    [Fact]
    public async Task ChangePassword_WithAWrongCurrentPassword_Is401WithTheSameDetailAsEmailChange_AndNothingChanges()
    {
        const string password = "TestPass123!";
        var email = _faker.Internet.Email();
        var bearer = await RegisterConfirmAndLogInAsync(email, password);

        var response = await Send(
            HttpMethod.Post,
            "/api/profile/password",
            bearer,
            new { oldPassword = "not-the-password", newPassword = "A-Fresh-Passphrase-9" }
        );

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        // The same detail string the email-change request uses for a wrong current
        // password — both password-gated forms on one client screen speak identically.
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.Equal("Your current password is incorrect.", problem!.Detail);

        // Nothing about the Profile changed: the current password still signs in and the
        // one that was offered as a replacement does not.
        Assert.Equal(HttpStatusCode.OK, (await LogIn(email, password)).StatusCode);
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await LogIn(email, "A-Fresh-Passphrase-9")).StatusCode
        );
    }

    [Fact]
    public async Task ChangePassword_CheckingTheCurrentPassword_CannotLockTheProfileOut()
    {
        const string password = "TestPass123!";
        var email = _faker.Internet.Email();
        var bearer = await RegisterConfirmAndLogInAsync(email, password);

        // Far more wrong attempts than the sign-in lockout threshold. CheckPasswordAsync
        // carries no lockout side effect, so none of these count against the Profile.
        for (var i = 0; i < 12; i++)
        {
            var attempt = await Send(
                HttpMethod.Post,
                "/api/profile/password",
                bearer,
                new { oldPassword = "still-not-it", newPassword = "A-Fresh-Passphrase-9" }
            );
            Assert.Equal(HttpStatusCode.Unauthorized, attempt.StatusCode);
        }

        // The current password still signs in — not 423 Locked.
        Assert.Equal(HttpStatusCode.OK, (await LogIn(email, password)).StatusCode);
    }

    [Fact]
    public async Task ChangePassword_WithANewPasswordFailingRegistrationsRule_IsValidationProblemNamingTheField_AndTheCurrentPasswordStillWorks()
    {
        const string password = "TestPass123!";
        var email = _faker.Internet.Email();
        var bearer = await RegisterConfirmAndLogInAsync(email, password);

        var response = await Send(
            HttpMethod.Post,
            "/api/profile/password",
            bearer,
            new { oldPassword = password, newPassword = "short" }
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>();
        Assert.Contains("NewPassword", problem!.Errors.Keys);

        // The rejected attempt never reached the change: the current password still
        // signs in and the weak one does not.
        Assert.Equal(HttpStatusCode.OK, (await LogIn(email, password)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await LogIn(email, "short")).StatusCode);
    }

    [Fact]
    public async Task ChangePassword_WithoutBearerToken_ReturnsUnauthorized()
    {
        var response = await _client.PostAsJsonAsync(
            "/api/profile/password",
            new { oldPassword = "TestPass123!", newPassword = "A-Fresh-Passphrase-9" }
        );

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // Read GET /api/profile with the given bearer — the Profile a client sees.
    private async Task<ProfileResponse> ReadProfile(string bearer)
    {
        var response = await Send(HttpMethod.Get, "/api/profile", bearer, body: null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var profile = await response.Content.ReadFromJsonAsync<ProfileResponse>();
        Assert.NotNull(profile);
        return profile;
    }

    // Request an email change with the given bearer and read the confirmation link back
    // out of the outbox — the userId and token the client would pull from the URL.
    private async Task<(int UserId, string Token)> RequestChangeAndReadLink(
        string bearer,
        string newEmail,
        string currentPassword
    )
    {
        var response = await Send(
            HttpMethod.Post,
            "/api/profile/email-change",
            bearer,
            new { newEmail, currentPassword }
        );
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var message = _emailSender.To(newEmail).Last();
        var match = Regex.Match(message.TextBody, @"userId=(?<userId>\d+)&token=(?<token>[^\s]+)");
        Assert.True(match.Success, $"No confirm-email-change link in:\n{message.TextBody}");

        return (
            int.Parse(match.Groups["userId"].Value),
            Uri.UnescapeDataString(match.Groups["token"].Value)
        );
    }

    // Register, pull the confirmation link out of the outbox, confirm, then log in —
    // the same arc the GetProfile_WithRealJwtFromRegister test proves, reused here to
    // get a real bearer token for a confirmed Profile.
    private async Task<string> RegisterConfirmAndLogInAsync(string email, string password)
    {
        var registerResponse = await _client.PostAsJsonAsync(
            "/api/auth/register",
            new
            {
                name = _faker.Person.FullName,
                email,
                password,
            }
        );
        Assert.Equal(HttpStatusCode.Created, registerResponse.StatusCode);

        var confirmMessage = Assert.Single(_emailSender.To(email));
        var match = Regex.Match(
            confirmMessage.TextBody,
            @"userId=(?<userId>\d+)&token=(?<token>[^\s]+)"
        );
        Assert.True(match.Success, $"No confirm-email link in:\n{confirmMessage.TextBody}");

        var confirmResponse = await _client.PostAsJsonAsync(
            "/api/auth/confirm-email",
            new
            {
                userId = int.Parse(match.Groups["userId"].Value),
                token = Uri.UnescapeDataString(match.Groups["token"].Value),
            }
        );
        Assert.Equal(HttpStatusCode.NoContent, confirmResponse.StatusCode);

        var loginResponse = await LogIn(email, password);
        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);

        var body = await loginResponse.Content.ReadFromJsonAsync<LoginResponse>();
        return body!.Token;
    }

    private Task<HttpResponseMessage> LogIn(string email, string password) =>
        _client.PostAsJsonAsync("/api/auth/login", new { email, password });

    private Task<HttpResponseMessage> Send(
        HttpMethod method,
        string uri,
        string bearerToken,
        object? body
    )
    {
        var request = new HttpRequestMessage(method, uri);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
        return _client.SendAsync(request);
    }

    private async Task<HttpResponseMessage> PutPictureAsync(string bearerToken, byte[] imageBytes)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, "/api/profile/picture");
        using var form = new MultipartFormDataContent();
        form.Add(new ByteArrayContent(imageBytes), "File", "source.png");
        request.Content = form;
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
        return await _client.SendAsync(request);
    }

    public void Dispose() => _scope.Dispose();
}
