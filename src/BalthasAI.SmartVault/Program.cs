using BalthasAI.SmartVault;
using SemanticPacker.Core.Services;

var builder = WebApplication.CreateBuilder(args);

// SmartVault 전역 서비스 등록
builder.Services.AddSmartVault(options =>
{
    options.DataPath = Path.Combine(Directory.GetCurrentDirectory(), "smartvault-data");
    options.EmbeddingDimension = 1024; // BGE-M3
});

// SemanticPacker 기반 청킹 활성화
builder.Services.UseSemanticChunking(options =>
{
    options.MaxChunkSize = 512;
    options.ChunkOverlap = 50;
});

// BGE-M3 임베딩 서비스 및 동기화 워커 활성화
builder.Services.UseEmbedding<BgeM3EmbeddingService>(
    syncInterval: TimeSpan.FromSeconds(30));

var app = builder.Build();

// WebDAV 엔드포인트 매핑
app.MapSmartVault("/dav", Path.Combine(Directory.GetCurrentDirectory(), "webdav-files"), options =>
{
    options.Realm = "BalthasAI SmartVault";
    options.UseBasicAuthentication(
        ("admin", "password123"),
        ("user", "user456")
    );

    options.DebounceDelayMs = 1000;
    options.MaxRetries = 3;

    // 처리할 파일 확장자 제한 (선택)
    // options.AllowedExtensions = [".txt", ".md", ".pdf"];
});

app.MapGet("/", () => "BalthasAI SmartVault");

app.Run();
