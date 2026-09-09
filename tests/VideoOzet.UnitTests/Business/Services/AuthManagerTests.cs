using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.Extensions.Configuration;
using Moq;
using VideoOzet.Business.DTOs;
using VideoOzet.Business.Helpers;
using VideoOzet.Business.Interfaces;
using VideoOzet.Business.Services;
using VideoOzet.Data.Entities;
using VideoOzet.Data.Repositories;
using MassTransit;
using Xunit;

namespace VideoOzet.UnitTests.Business.Services;

public class AuthManagerTests
{
    private readonly Mock<IUserRepository> _userRepoMock;
    private readonly Mock<IValidator<UserRegisterDto>> _validatorMock;
    private readonly Mock<IEmailService> _emailServiceMock;
    private readonly Mock<IPublishEndpoint> _publishEndpointMock;
    private readonly Mock<IConfiguration> _configMock;
    private readonly Mock<ILogService> _logServiceMock;
    private readonly AuthManager _authManager;

    public AuthManagerTests()
    {
        _userRepoMock = new Mock<IUserRepository>();
        _validatorMock = new Mock<IValidator<UserRegisterDto>>();
        _emailServiceMock = new Mock<IEmailService>();
        _publishEndpointMock = new Mock<IPublishEndpoint>();
        _configMock = new Mock<IConfiguration>();
        _logServiceMock = new Mock<ILogService>();

        _authManager = new AuthManager(
            _userRepoMock.Object,
            _validatorMock.Object,
            _emailServiceMock.Object,
            _publishEndpointMock.Object,
            _configMock.Object,
            _logServiceMock.Object);
    }

    [Fact]
    public async Task RegisterAsync_WithInvalidData_ReturnsValidationError()
    {
        // Arrange
        var dto = new UserRegisterDto { Email = "invalid", Password = "123", FullName = "Test" };
        var validationResult = new FluentValidation.Results.ValidationResult(new[] { new ValidationFailure("Email", "Geçersiz e-posta formatı.") });
        
        _validatorMock.Setup(v => v.ValidateAsync(dto, default)).ReturnsAsync(validationResult);

        // Act
        var result = await _authManager.RegisterAsync(dto);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Geçersiz e-posta formatı.");
        _userRepoMock.Verify(x => x.AddAsync(It.IsAny<User>()), Times.Never);
    }

    [Fact]
    public async Task RegisterAsync_WithExistingEmail_ReturnsError()
    {
        // Arrange
        var dto = new UserRegisterDto { Email = "test@test.com", Password = "Password123!", FullName = "Test" };
        
        _validatorMock.Setup(v => v.ValidateAsync(dto, default)).ReturnsAsync(new FluentValidation.Results.ValidationResult());
        _userRepoMock.Setup(r => r.IsEmailUniqueAsync(dto.Email)).ReturnsAsync(false);

        // Act
        var result = await _authManager.RegisterAsync(dto);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Be("Bu e-posta adresi zaten kayıtlı.");
        _userRepoMock.Verify(x => x.AddAsync(It.IsAny<User>()), Times.Never);
    }

    [Fact]
    public async Task RegisterAsync_WithValidData_SavesUserAndGivesWelcomeTokens()
    {
        // Arrange
        var dto = new UserRegisterDto { Email = "new@test.com", Password = "Password123!", FullName = "New User" };
        
        _validatorMock.Setup(v => v.ValidateAsync(dto, default)).ReturnsAsync(new FluentValidation.Results.ValidationResult());
        _userRepoMock.Setup(r => r.IsEmailUniqueAsync(dto.Email)).ReturnsAsync(true);
        _userRepoMock.Setup(r => r.AddAsync(It.IsAny<User>())).ReturnsAsync((User u) => u);
        _userRepoMock.Setup(r => r.SaveChangesAsync()).ReturnsAsync(1);

        // Act
        var result = await _authManager.RegisterAsync(dto);

        // Assert
        result.Success.Should().BeTrue();
        result.User.Should().NotBeNull();
        result.User!.Email.Should().Be(dto.Email);
        result.User.TokenBalance.Should().Be(3); // Hoş geldin hediyesi

        _userRepoMock.Verify(x => x.AddAsync(It.Is<User>(u => 
            u.Email == dto.Email && 
            u.FullName == dto.FullName &&
            u.TokenBalance == 3
        )), Times.Once);
        _userRepoMock.Verify(x => x.SaveChangesAsync(), Times.Once);
        _emailServiceMock.Verify(x => x.SendTemplatedEmailAsync(dto.Email, "Hoş Geldiniz!", It.IsAny<Dictionary<string, string>>()), Times.Once);
    }

    [Fact]
    public async Task LoginAsync_WithWrongEmail_ReturnsError()
    {
        // Arrange
        var dto = new UserLoginDto { Email = "wrong@test.com", Password = "Password123!" };
        _userRepoMock.Setup(r => r.GetByEmailAsync(dto.Email)).ReturnsAsync((User?)null);

        // Act
        var result = await _authManager.LoginAsync(dto);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Be("E-posta veya şifre hatalı.");
    }

    [Fact]
    public async Task LoginAsync_WithWrongPassword_ReturnsError()
    {
        // Arrange
        var dto = new UserLoginDto { Email = "test@test.com", Password = "WrongPassword!" };
        var user = new User 
        { 
            Email = dto.Email, 
            PasswordHash = PasswordHasher.Hash("CorrectPassword123!") // Gerçek şifre bu
        };
        
        _userRepoMock.Setup(r => r.GetByEmailAsync(dto.Email)).ReturnsAsync(user);

        // Act
        var result = await _authManager.LoginAsync(dto);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Be("E-posta veya şifre hatalı.");
    }

    [Fact]
    public async Task LoginAsync_WithValidCredentials_ReturnsSuccess()
    {
        // Arrange
        var dto = new UserLoginDto { Email = "test@test.com", Password = "Password123!" };
        var user = new User 
        { 
            Id = Guid.NewGuid(),
            Email = dto.Email, 
            FullName = "Test User",
            PasswordHash = PasswordHasher.Hash(dto.Password),
            IsActive = true
        };
        
        _userRepoMock.Setup(r => r.GetByEmailAsync(dto.Email)).ReturnsAsync(user);

        // Act
        var result = await _authManager.LoginAsync(dto);

        // Assert
        result.Success.Should().BeTrue();
        result.User.Should().NotBeNull();
        result.User!.Email.Should().Be(dto.Email);
    }

    [Fact]
    public async Task LoginAsync_WhenAccountIsInactive_ReturnsError()
    {
        // Arrange
        var dto = new UserLoginDto { Email = "test@test.com", Password = "Password123!" };
        var user = new User 
        { 
            Email = dto.Email, 
            PasswordHash = PasswordHasher.Hash(dto.Password),
            IsActive = false // Askıya alınmış hesap
        };
        
        _userRepoMock.Setup(r => r.GetByEmailAsync(dto.Email)).ReturnsAsync(user);

        // Act
        var result = await _authManager.LoginAsync(dto);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Be("Hesabınız askıya alınmıştır.");
    }
}
