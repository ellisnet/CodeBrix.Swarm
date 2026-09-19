using System;
using CodeBrix.Swarm.Core;
using CodeBrix.Swarm.Queen.Authentication;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Swarm.Queen.Tests.Authentication;

public class SwarmTokenAuthorityTests
{
    private const string Secret = "a-master-secret-long-enough-for-a-swarm";
    private const string OtherSecret = "a-different-master-secret-long-enough-too";

    private static SwarmTokenAuthority Authority() => new(Secret);

    [Fact]
    public void a_minted_token_reads_back_as_what_it_was_minted_as()
    {
        //Arrange
        var authority = Authority();

        //Act
        var token = authority.CreateToken(SwarmRole.Worker);
        var read = authority.ReadToken(SwarmRole.Worker, token);

        //Assert
        read.Role.Should().Be(SwarmRole.Worker);
        read.TokenId.Should().NotBe(Guid.Empty);
        read.ExpiresUtc.Should().BeNull();
        read.IssuedUtc.Kind.Should().Be(DateTimeKind.Utc);
    }

    [Fact]
    public void every_token_is_different_even_for_the_same_role()
    {
        //Arrange
        var authority = Authority();

        //Act
        var first = authority.CreateToken(SwarmRole.Hive);
        var second = authority.CreateToken(SwarmRole.Hive);

        //Assert
        first.Should().NotBe(second);
        authority.ReadToken(SwarmRole.Hive, first).TokenId
            .Should().NotBe(authority.ReadToken(SwarmRole.Hive, second).TokenId);
    }

    [Fact]
    public void a_hive_token_is_refused_when_it_is_presented_as_a_worker_token()
    {
        //Arrange
        var authority = Authority();
        var token = authority.CreateToken(SwarmRole.Hive);

        //Act
        var act = () => authority.ReadToken(SwarmRole.Worker, token);

        //Assert
        act.Should().Throw<SwarmTokenException>();
    }

    [Fact]
    public void a_worker_token_is_refused_when_it_is_presented_as_a_hive_token()
    {
        //Arrange
        var authority = Authority();
        var token = authority.CreateToken(SwarmRole.Worker);

        //Act
        var act = () => authority.ReadToken(SwarmRole.Hive, token);

        //Assert
        act.Should().Throw<SwarmTokenException>();
    }

    [Fact]
    public void a_token_minted_from_another_secret_is_refused()
    {
        //Arrange
        var token = new SwarmTokenAuthority(OtherSecret).CreateToken(SwarmRole.Worker);

        //Act
        var act = () => Authority().ReadToken(SwarmRole.Worker, token);

        //Assert
        act.Should().Throw<SwarmTokenException>();
    }

    [Fact]
    public void a_token_cannot_be_minted_already_expired()
    {
        //Arrange
        var authority = Authority();

        //Act
        var act = () => authority.CreateToken(SwarmRole.Worker, DateTime.UtcNow.AddHours(-1));

        //Assert
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void a_token_whose_life_has_run_out_is_refused()
    {
        //Arrange - minted with an expiry that has already passed by the time it is read.
        var authority = Authority();
        var token = authority.CreateToken(SwarmRole.Worker, TimeSpan.FromSeconds(1));

        //Act
        var act = () =>
        {
            System.Threading.Thread.Sleep(TimeSpan.FromMilliseconds(1100));
            return authority.ReadToken(SwarmRole.Worker, token);
        };

        //Assert
        act.Should().Throw<SwarmTokenException>();
    }

    [Fact]
    public void a_token_with_a_lifetime_carries_an_expiry()
    {
        //Arrange
        var authority = Authority();

        //Act
        var read = authority.ReadToken(
            SwarmRole.Hive, authority.CreateToken(SwarmRole.Hive, TimeSpan.FromHours(2)));

        //Assert
        read.ExpiresUtc.Should().NotBeNull();
        read.ExpiresUtc.Value.Should().BeAfter(DateTime.UtcNow.AddMinutes(100));
    }

    [Fact]
    public void a_lifetime_of_nothing_is_refused()
    {
        //Arrange
        var authority = Authority();

        //Act
        var act = () => authority.CreateToken(SwarmRole.Worker, TimeSpan.Zero);

        //Assert
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void a_tampered_token_is_refused()
    {
        //Arrange
        var authority = Authority();
        var token = authority.CreateToken(SwarmRole.Worker);
        var characters = token.ToCharArray();
        characters[^1] = characters[^1] == 'A' ? 'B' : 'A';
        var tampered = new string(characters);

        //Act
        var act = () => authority.ReadToken(SwarmRole.Worker, tampered);

        //Assert
        act.Should().Throw<SwarmTokenException>();
    }

    [Theory]
    [InlineData("not-a-token")]
    [InlineData("!!!!")]
    [InlineData("AAAA")]
    public void something_that_is_not_a_token_is_refused(string token)
    {
        //Arrange
        var authority = Authority();

        //Act
        var act = () => authority.ReadToken(SwarmRole.Worker, token);

        //Assert
        act.Should().Throw<SwarmTokenException>();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void a_missing_token_is_refused(string token)
    {
        //Arrange
        var authority = Authority();

        //Act
        var act = () => authority.ReadToken(SwarmRole.Worker, token);

        //Assert
        act.Should().Throw<SwarmTokenException>();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("      ")]
    [InlineData("too-short")]
    [InlineData("0123456789012345678901234567890")]
    public void a_missing_or_short_master_secret_is_refused(string secret)
    {
        //Act
        var act = () => new SwarmTokenAuthority(secret);

        //Assert
        act.Should().Throw<SwarmTokenException>();
    }

    [Fact]
    public void a_master_secret_of_exactly_the_minimum_length_is_accepted()
    {
        //Arrange
        var secret = new string('s', SwarmTokenAuthority.MinimumMasterSecretLength);

        //Act
        var act = () => new SwarmTokenAuthority(secret);

        //Assert
        act.Should().NotThrow();
    }

    [Fact]
    public void TryReadToken_says_no_rather_than_throwing()
    {
        //Arrange
        var authority = Authority();

        //Act
        var accepted = authority.TryReadToken(
            SwarmRole.Worker, authority.CreateToken(SwarmRole.Hive), out var read);

        //Assert
        accepted.Should().BeFalse();
        read.Should().BeNull();
    }

    [Fact]
    public void TryReadToken_says_yes_for_a_good_token()
    {
        //Arrange
        var authority = Authority();

        //Act
        var accepted = authority.TryReadToken(
            SwarmRole.Hive, authority.CreateToken(SwarmRole.Hive), out var read);

        //Assert
        accepted.Should().BeTrue();
        read.Role.Should().Be(SwarmRole.Hive);
    }

    [Fact]
    public void a_token_is_url_safe_so_it_survives_a_query_string()
    {
        //Arrange
        var authority = Authority();

        //Act
        var token = authority.CreateToken(SwarmRole.Worker);

        //Assert
        token.Should().NotContain("+");
        token.Should().NotContain("/");
        token.Should().NotContain("=");
        Uri.EscapeDataString(token).Should().Be(token);
    }

    [Fact]
    public void the_two_roles_do_not_share_a_key()
    {
        //Arrange - the same authority, the same secret, the two roles.
        var authority = Authority();

        //Act
        var hiveToken = authority.CreateToken(SwarmRole.Hive);
        var workerToken = authority.CreateToken(SwarmRole.Worker);

        //Assert - neither reads at the other's hub, which is the key derivation refusing first.
        authority.TryReadToken(SwarmRole.Worker, hiveToken, out _).Should().BeFalse();
        authority.TryReadToken(SwarmRole.Hive, workerToken, out _).Should().BeFalse();
        authority.TryReadToken(SwarmRole.Hive, hiveToken, out _).Should().BeTrue();
        authority.TryReadToken(SwarmRole.Worker, workerToken, out _).Should().BeTrue();
    }

    [Fact]
    public void an_unknown_role_is_refused()
    {
        //Arrange
        var authority = Authority();

        //Act
        var act = () => authority.CreateToken((SwarmRole)99);

        //Assert
        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
