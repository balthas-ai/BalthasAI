# Balthas AI

> Sovereign AI for Your Private Knowledge Vault.

**Balthas AI(발타스 AI)**는 외부 클라우드 의존성을 최소/제거하는 **Zero-Cloud** 지향의 개인용 AI 지식 금고(장기적으로는 personal AI RAG)를 목표로 합니다.

이 리포지터리는 그중 **첫 번째 코어 컴포넌트인 SemanticPacker**를 먼저 공개/출시하는 단계입니다. SemanticPacker는 문서(텍스트)를 **시맨틱(semantic) 단위로 청킹(chunking)** 한 뒤, 결과를 **Parquet 파일**로 저장하는 .NET 라이브러리/CLI입니다.

- CLI: 파일/디렉터리/URL 입력을 청킹하여 `*.chunks.parquet` 생성
- Core: 추출(Extractor) → 시맨틱 청킹(Chunker) → 저장(Storage) 파이프라인
- Embedding: **모델/구현 교체가 가능**하며, 기본 구현은 **BGE-M3 ONNX**를 사용(청킹 경계 판단용, 기본 출력에는 임베딩을 저장하지 않음)

## 임베딩 모델 철학(범용성 + CPU-only)

SemanticPacker는 특정 벤더/클라우드 모델에 고정되지 않도록 `IEmbeddingService` 추상화로 설계되어, 원하는 임베딩 모델/런타임으로 교체할 수 있습니다.

다만 기본 구현은 **BGE-M3를 ONNX로 구동**하는 형태(`BgeM3EmbeddingService`)를 채택했습니다.

- **범용성**: 다국어/범용 임베딩 모델 계열을 기본으로 삼아 다양한 문서 도메인에 적용 가능하도록 지향
- **CPU-only 실행**: ONNX Runtime 기반으로 **GPU/NPU 없이도(=CPU만으로도) 임베딩 생성**이 가능해, 로컬/서버/NAS 환경에서의 휴대성과 재현성을 높임

> 요약: “모델은 바꿀 수 있게 열어두되, 기본값은 CPU-only로도 돌아가는 범용 임베딩”을 목표로 합니다.

## 상태(현재 범위)

현재 공개된 범위는 “SemanticPacker 코어”에 집중합니다.

- 지원 입력(기본): 텍스트 계열 파일 및 URL(HTML/텍스트를 문자열로 받아 청킹)
- 출력: Zstd 압축 Parquet(청크 텍스트 + 메타데이터)
- 임베딩/추론: 로컬 ONNX 기반(기본 구현 BGE-M3)

아래 항목들은 **브랜드 비전에 포함되지만, 이 리포지터리에서 아직 구현/공개된 기능이라고 단정하지 않습니다**(로드맵 참조).

- HWP/PDF 네이티브 파싱 기반 Ingestion
- WebDAV 인터페이스 및 “지식 금고” 액세스 경로
- 개인 RAG(색인/검색/리트리벌/LLM 추론) 전체 파이프라인

## 프로젝트 구성

- `src/BalthasAI.SemanticPacker.Abstractions/`
  - 핵심 계약(interfaces) 및 모델 정의
- `src/BalthasAI.SemanticPacker.Core/`
  - 문서 처리 파이프라인(`DocumentProcessor`), 시맨틱 청킹(`SemanticChunker`), Parquet 저장(`ParquetChunkStorage`), BGE-M3 임베딩(`BgeM3EmbeddingService`)
- `src/BalthasAI.SemanticPacker.Extractors/`
  - 기본 텍스트 추출기(`PlainTextExtractor`)
- `src/BalthasAI.SemanticPacker.Cli/`
  - `semanticpacker` CLI 엔트리포인트
- `src/BalthasAI.SemanticPacker.Test/`
  - MSTest 기반 테스트

## 빠른 시작

### 요구사항

- .NET SDK `10.0` (프로젝트 TargetFramework: `net10.0`)
- (선택) BGE-M3 ONNX 모델 폴더

### 빌드

```bash
dotnet build .\src\BalthasAI.SemanticPacker.Cli\BalthasAI.SemanticPacker.Cli.csproj
```

### 테스트

```bash
dotnet test .\src\BalthasAI.SemanticPacker.Test\BalthasAI.SemanticPacker.Test.csproj
```

## CLI 사용법

CLI는 다음 3가지 커맨드를 제공합니다.

- `file <files...>`: 파일(들) 청킹
- `dir <dirs...>` 또는 `directory <dirs...>`: 디렉터리 내 파일 배치 처리
- `url <urls...>`: 웹 페이지 다운로드 후 청킹

실행 예시(개발 중):

PowerShell(Windows):

```powershell
$env:BGE_M3_MODEL_PATH = "E:\\bge-m3"
dotnet run --project .\src\BalthasAI.SemanticPacker.Cli -- file .\docs\document.txt -o .\output
```

bash 계열 셸:

```bash
# 파일 청킹
BGE_M3_MODEL_PATH=E:\bge-m3 dotnet run --project .\src\BalthasAI.SemanticPacker.Cli -- file .\docs\document.txt -o .\output

# 디렉터리 청킹(재귀, 패턴)
BGE_M3_MODEL_PATH=E:\bge-m3 dotnet run --project .\src\BalthasAI.SemanticPacker.Cli -- dir .\docs -r -p "*.md" -o .\output

# URL 청킹
BGE_M3_MODEL_PATH=E:\bge-m3 dotnet run --project .\src\BalthasAI.SemanticPacker.Cli -- url https://example.com/page.html -o .\output
```

### 옵션

공통 옵션:

- `-o, --output <dir>`: 출력 디렉터리
- `-f, --force`: 기존 결과 파일 덮어쓰기
- `-v, --verbose`: 디버그 로그 출력
- `-t, --threshold <n>`: 문장 간 유사도 임계값(기본 `0.5`)
- `--min-chunk <n>`: 최소 청크 길이(기본 `50`)
- `--max-chunk <n>`: 최대 청크 길이(기본 `500`)
- `--version <ver>`: 메타데이터 버전(기본 `1.0.0`)

디렉터리 전용 옵션:

- `-r, --recursive`: 하위 디렉터리 포함
- `-p, --pattern <pat>`: 검색 패턴(기본 `*.*`)

### 환경 변수

- `BGE_M3_MODEL_PATH`: (기본 임베딩 구현을 사용할 때) BGE-M3 모델 루트 경로
  - 환경 변수 미설정 시 기본값은 코드상 `E:\bge-m3` 입니다.

참고: 다른 임베딩 구현으로 교체하는 경우, 요구되는 모델 경로/설정은 달라질 수 있습니다.

## 입력 지원(기본 Extractor)

기본 제공 `PlainTextExtractor`는 아래 확장자를 처리합니다.

- `.txt`, `.md`, `.markdown`, `.csv`, `.json`, `.xml`, `.html`, `.htm`, `.log`, `.ini`, `.cfg`, `.yaml`, `.yml`

추가 Extractor는 DI로 `ITextExtractor`를 구현해 등록할 수 있습니다.

## 출력 포맷 (Parquet)

기본 저장소 구현은 `ParquetChunkStorage`이며, 결과를 Zstd 압축 Parquet으로 저장합니다.

- 기본 출력 파일명: `{원본파일명}.chunks.parquet`
- 출력 디렉터리: `-o/--output` 미지정 시 입력 파일과 같은 디렉터리

### 스키마(컬럼)

Parquet에는 청크 텍스트와 추적용 메타데이터가 함께 저장됩니다.

- `id`: `source_id + content_hash` 기반의 deterministic ID
- `content_hash`: 청크 텍스트 SHA-256
- `source_id`, `source_name`, `version`, `created_at`
- `source_content_type`, `source_file_size`, `source_file_hash`
- `text`, `chunk_index`
- `start_index`, `end_index`, `page_number`, `source_location`

## 동작 개요

1. **추출(Extract)**: 입력(파일/스트림)에서 텍스트를 추출 (`ITextExtractor`)
2. **청킹(Chunk)**:
   - 문장 단위로 분할(기본 구분자: `. ! ?` 및 CJK `。！？`, 그리고 `\n\n`)
   - 각 문장의 임베딩을 생성(`IEmbeddingService`)하고, 인접 문장 간 cosine similarity를 계산
   - similarity가 임계값(`--threshold`)보다 낮으면 경계로 간주
   - `--min-chunk`, `--max-chunk` 기준을 반영해 최종 청크 생성
3. **저장(Store)**: Parquet으로 저장 (`IChunkStorage`)

> 임베딩은 청킹 경계 판단을 위해 내부적으로만 사용하며, 기본 저장 포맷에는 임베딩 벡터를 포함하지 않습니다.

## 로드맵(지향점)

SemanticPacker는 Balthas AI의 “Zero‑Cloud 개인 지식 금고”를 만들기 위한 기반 컴포넌트입니다. 장기적으로는 다음 방향을 목표로 합니다.

- **Native Ingestion**: HWP/PDF 등 국내 문서 환경을 포함한 고정밀 파서 확장
  - 예: `HwpLibSharp` 기반 `ITextExtractor` 통합(Extractor 확장)
- **Portable & Simple**: 단일 바이너리/간편 배포를 통한 로컬·NAS 친화 운영
- **Secure Access**: 로컬 우선 + 안전한 접근 경로(WebDAV 등)와 권장 네트워크 아키텍처
  - 예: ASP.NET Core(Kestrel) 기반 고성능 WebDAV 지원을 통한 손쉬운 RAG Integration 관리
- **Sovereign Intelligence**: 모든 임베딩/리트리벌/추론이 사용자 통제 하에 로컬에서 완결되는 개인 RAG

## 라이선스

이 리포지터리는 **Open Core(혼합 라이선스)** 모델을 지향합니다.

- **라이브러리(코어) 파트**: Apache License 2.0
  - `BalthasAI.SemanticPacker.Abstractions` (Apache-2.0)
  - `BalthasAI.SemanticPacker.Core` (Apache-2.0)
  - `BalthasAI.SemanticPacker.Extractors` (Apache-2.0)
- **CLI 및 향후 솔루션 파트**: AGPL v3.0 + 상용(Commercial) 듀얼 라이선스
  - `BalthasAI.SemanticPacker.Cli` (AGPL-3.0 또는 상용 라이선스)

상용(Commercial) 라이선스 문의: rkttu@rkttu.com

라이선스 전문:

- Apache 2.0: `LICENSES/Apache-2.0.txt`
- AGPL 3.0: `LICENSES/AGPL-3.0.txt`

프로젝트별 라이선스 고지는 각 프로젝트 폴더의 `LICENSE` 파일을 참고하세요.
