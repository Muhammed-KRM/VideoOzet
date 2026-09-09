using System;
using System.Threading.Tasks;
using FluentAssertions;
using FluentValidation;
using FluentValidation.Results;
using MassTransit;
using Moq;
using VideoOzet.Business.DTOs;
using VideoOzet.Business.Exceptions;
using VideoOzet.Business.Interfaces;
using VideoOzet.Business.Services;
using VideoOzet.Data.Entities;
using VideoOzet.Data.Repositories;
using Xunit;

namespace VideoOzet.UnitTests.Business.Services;

public class ListingManagerTests
{
    private readonly Mock<IListingRepository> _listingRepoMock;
    private readonly Mock<IRepository<Branch>> _branchRepoMock;
    private readonly Mock<IRepository<District>> _districtRepoMock;
    private readonly Mock<IValidator<ListingCreateDto>> _validatorMock;
    private readonly Mock<IPublishEndpoint> _publishEndpointMock;
    private readonly Mock<ITokenService> _tokenServiceMock;
    private readonly Mock<ISettingService> _settingServiceMock;
    private readonly Mock<ILogService> _logServiceMock;
    private readonly Mock<IModerationService> _moderationServiceMock;
    private readonly ListingManager _listingManager;

    public ListingManagerTests()
    {
        _listingRepoMock = new Mock<IListingRepository>();
        _branchRepoMock = new Mock<IRepository<Branch>>();
        _districtRepoMock = new Mock<IRepository<District>>();
        _validatorMock = new Mock<IValidator<ListingCreateDto>>();
        _publishEndpointMock = new Mock<IPublishEndpoint>();
        _tokenServiceMock = new Mock<ITokenService>();
        _settingServiceMock = new Mock<ISettingService>();
        _logServiceMock = new Mock<ILogService>();
        _moderationServiceMock = new Mock<IModerationService>();

        // Default: moderation returns clean
        _moderationServiceMock
            .Setup(m => m.CheckContent(It.IsAny<string>(), It.IsAny<string>()))
            .Returns(ModerationResult.Clean());

        _listingManager = new ListingManager(
            _listingRepoMock.Object,
            _branchRepoMock.Object,
            _districtRepoMock.Object,
            _validatorMock.Object,
            _publishEndpointMock.Object,
            _tokenServiceMock.Object,
            _settingServiceMock.Object,
            _logServiceMock.Object,
            _moderationServiceMock.Object);
    }

    [Fact]
    public async Task CreateAsync_WithInvalidData_ThrowsBusinessException()
    {
        // Arrange
        var dto = new ListingCreateDto { Title = "" };
        var validationResult = new FluentValidation.Results.ValidationResult(new[] { new ValidationFailure("Title", "Başlık zorunludur.") });
        _validatorMock.Setup(v => v.ValidateAsync(dto, default)).ReturnsAsync(validationResult);

        // Act
        Func<Task> act = async () => await _listingManager.CreateAsync(dto, Guid.NewGuid());

        // Assert
        await act.Should().ThrowAsync<BusinessException>().WithMessage("*Başlık zorunludur.*");
        _listingRepoMock.Verify(r => r.AddAsync(It.IsAny<Listing>()), Times.Never);
    }

    [Fact]
    public async Task CreateAsync_WithValidData_ReturnsListingDto()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var dto = new ListingCreateDto 
        { 
            Title = "Matematik Dersi", 
            Description = "Açıklama alanı", 
            BranchId = 1, 
            DistrictId = 1, 
            HourlyPrice = 500,
            Type = VideoOzet.Data.Enums.ListingType.TeacherOffering
        };

        _validatorMock.Setup(v => v.ValidateAsync(dto, default)).ReturnsAsync(new FluentValidation.Results.ValidationResult());
        _settingServiceMock.Setup(s => s.GetIntSettingAsync("ListingCreationCost", 5)).ReturnsAsync(5);
        _tokenServiceMock.Setup(t => t.SpendTokenAsync(userId, 5, It.IsAny<string>())).Returns(Task.CompletedTask);
        _listingRepoMock.Setup(r => r.AddAsync(It.IsAny<Listing>())).ReturnsAsync((Listing l) => l);
        _listingRepoMock.Setup(r => r.SaveChangesAsync()).ReturnsAsync(1);

        // Act
        var result = await _listingManager.CreateAsync(dto, userId);

        // Assert
        result.Should().NotBeNull();
        result.Title.Should().Be("Matematik Dersi");
        // Slug generation is tested here intrinsically
        result.Slug.Should().Be("matematik-dersi"); 
        
        _listingRepoMock.Verify(r => r.AddAsync(It.Is<Listing>(l => l.OwnerId == userId && l.Title == dto.Title)), Times.Once);
        _listingRepoMock.Verify(r => r.SaveChangesAsync(), Times.Once);
    }
}
