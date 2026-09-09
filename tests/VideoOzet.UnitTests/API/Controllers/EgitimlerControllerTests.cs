using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Moq;
using VideoOzet.API.Controllers;
using VideoOzet.Business.DTOs.Egitim;
using VideoOzet.Business.Interfaces;

namespace VideoOzet.UnitTests.API.Controllers;

public class EgitimlerControllerTests
{
    private readonly Mock<IEgitimService> _mockEgitimService;
    private readonly EgitimlerController _controller;

    public EgitimlerControllerTests()
    {
        _mockEgitimService = new Mock<IEgitimService>();
        _controller = new EgitimlerController(_mockEgitimService.Object);
    }

    [Fact]
    public async Task GetAll_ShouldReturnOkWithList()
    {
        // Arrange
        var dtoList = new List<EgitimListDto> { new EgitimListDto { Id = Guid.NewGuid(), Ad = "Test" } };
        _mockEgitimService.Setup(s => s.GetAllAsync()).ReturnsAsync(dtoList);

        // Act
        var result = await _controller.GetAll();

        // Assert
        var okResult = result as OkObjectResult;
        okResult.Should().NotBeNull();
        okResult!.StatusCode.Should().Be(200);
        okResult.Value.Should().BeEquivalentTo(dtoList);
    }

    [Fact]
    public async Task GetById_ShouldReturnOkWithDto()
    {
        // Arrange
        var id = Guid.NewGuid();
        var dto = new EgitimDetailDto { Id = id, Ad = "Test" };
        _mockEgitimService.Setup(s => s.GetByIdAsync(id)).ReturnsAsync(dto);

        // Act
        var result = await _controller.GetById(id);

        // Assert
        var okResult = result as OkObjectResult;
        okResult.Should().NotBeNull();
        okResult!.StatusCode.Should().Be(200);
        okResult.Value.Should().BeEquivalentTo(dto);
    }

    [Fact]
    public async Task Create_ShouldReturnCreatedAtAction()
    {
        // Arrange
        var createDto = new EgitimCreateDto { Ad = "Test" };
        var detailDto = new EgitimDetailDto { Id = Guid.NewGuid(), Ad = "Test" };
        _mockEgitimService.Setup(s => s.CreateAsync(createDto)).ReturnsAsync(detailDto);

        // Act
        var result = await _controller.Create(createDto);

        // Assert
        var createdResult = result as CreatedAtActionResult;
        createdResult.Should().NotBeNull();
        createdResult!.StatusCode.Should().Be(201);
        createdResult.Value.Should().BeEquivalentTo(detailDto);
        createdResult.RouteValues!["id"].Should().Be(detailDto.Id);
    }

    [Fact]
    public async Task Update_ShouldReturnOk_WhenIdMatches()
    {
        // Arrange
        var id = Guid.NewGuid();
        var updateDto = new EgitimUpdateDto { Id = id, Ad = "Test" };
        var detailDto = new EgitimDetailDto { Id = id, Ad = "Test" };
        _mockEgitimService.Setup(s => s.UpdateAsync(updateDto)).ReturnsAsync(detailDto);

        // Act
        var result = await _controller.Update(id, updateDto);

        // Assert
        var okResult = result as OkObjectResult;
        okResult.Should().NotBeNull();
        okResult!.StatusCode.Should().Be(200);
        okResult.Value.Should().BeEquivalentTo(detailDto);
    }

    [Fact]
    public async Task Update_ShouldReturnBadRequest_WhenIdDoesNotMatch()
    {
        // Arrange
        var id = Guid.NewGuid();
        var updateDto = new EgitimUpdateDto { Id = Guid.NewGuid(), Ad = "Test" };

        // Act
        var result = await _controller.Update(id, updateDto);

        // Assert
        var badRequestResult = result as BadRequestObjectResult;
        badRequestResult.Should().NotBeNull();
        badRequestResult!.StatusCode.Should().Be(400);
    }

    [Fact]
    public async Task Delete_ShouldReturnNoContent()
    {
        // Arrange
        var id = Guid.NewGuid();
        _mockEgitimService.Setup(s => s.DeleteAsync(id)).Returns(Task.CompletedTask);

        // Act
        var result = await _controller.Delete(id);

        // Assert
        var noContentResult = result as NoContentResult;
        noContentResult.Should().NotBeNull();
        noContentResult!.StatusCode.Should().Be(204);
    }
}
