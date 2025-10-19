# Notes Service - Technical Assignment

A full-stack notes management application with AI-powered summarization, built for the TFS Complex Integrations Team.

## 📋 Table of Contents
- [Overview](#overview)
- [Features](#features)
- [Architecture](#architecture)
- [Tech Stack](#tech-stack)
- [Design Decisions](#design-decisions)
- [Setup Instructions](#setup-instructions)
- [API Documentation](#api-documentation)
- [Project Structure](#project-structure)

---

## 🎯 Overview

This is a serverless notes application that allows users to create, read, update, and delete notes with advanced search and sorting capabilities. The application features asynchronous AI-powered summarization to provide automatic summaries of note content.

**Live Demo:** [Your deployed URL]  
**GitHub Repository:** [Your repo URL]

---

## ✨ Features

### Core Features ✅
- **Complete CRUD Operations**: Create, Read, Update, and Delete notes
- **Search Functionality**: Full-text search across note content
- **Sort Functionality**: Sort notes by date, title, or relevance
- **Responsive UI**: Works seamlessly on desktop and mobile devices

### Nice-to-Have Features ✅
- **AI Summarization**: Automatic AI-generated summaries for notes
- **Asynchronous Processing**: Non-blocking summarization using event-driven architecture
- **Real-time Updates**: Notes appear immediately while summaries generate in background

---

## 🏗 Architecture

### High-Level Architecture Diagram

```
┌──────────┐
│ Frontend │
│(React/   │
│ Angular) │
└─────┬────┘
      │
      ▼
┌─────────────┐
│ API Gateway │
│  (Backend)  │
└──┬────┬────┬┘
   │    │    │
   ▼    ▼    ▼
┌────┐ ┌────┐ ┌────┐
│POST│ │GET │ │DEL │
│    │ │    │ │    │
└─┬──┘ └──┬─┘ └──┬─┘
  │       │      │
  ▼       ▼      ▼
┌─────────────────────┐
│  Lambda: Note-CRUD  │
└────────┬────────────┘
         │
         ▼
    ┌────────┐
    │  DAL   │
    │(Data   │
    │Access  │
    │Layer)  │
    └───┬────┘
        │
        ▼
   ┌─────────────┐
   │ PostgreSQL  │
   └─────────────┘
   
After Successful Save:
┌──────────────┐
│ Lambda:      │
│ Upsert       │
└──────┬───────┘
       │
       ▼
   ┌───────┐
   │  SNS  │
   │ Topic │
   └───┬───┘
       │
       ▼
   ┌───────┐
   │  SQS  │
   │ Queue │
   └───┬───┘
       │
       ▼
┌──────────────┐
│ Lambda:      │
│ Summarize    │
└──────┬───────┘
       │
       ▼
   ┌────────┐
   │  DAL   │
   └───┬────┘
       │
       ▼
   ┌─────────────┐
   │ PostgreSQL  │
   └─────────────┘
```

### Architecture Flow


1. **Frontend → API Backend**: User interacts with the application
2. **API Backend → NOTE CRUD Lambda**: Single Lambda handles all CRUD operations
   - GET /notes - List/search notes with sorting
   - GET /notes/{id} - Fetch single note
   - POST /notes - Create new note
   - PUT /notes/{id} - Update existing note
   - DELETE /notes/{id} - Delete note
3. **Lambda → DAL → Database**: All database operations abstracted through DAL
4. **Event-Driven Summarization** (after successful save):
   - Lambda publishes event to SNS
   - SQS receives message
   - Summarize Lambda processes asynchronously
   - Summary updates note via DAL
   
---

## 🛠 Tech Stack

### Frontend
- **Framework**: Angular 17
- **Styling**: Tailwind CSS
- **State Management**: React Context API / Redux
- **HTTP Client**: Axios

### Backend
- **.NET**: 8
- **Language**: C#
- **Framework**: ASP.NET Core Web API

### AWS Services
- **Lambda**: Serverless compute for business logic
- **API Gateway**: RESTful API endpoint management
- **SNS**: Publish/subscribe messaging for events
- **SQS**: Message queue for reliable async processing
- **CloudFormation/CDK**: Infrastructure as Code

### Database
- **PostgreSQL**: Relational database (Docker containerized)
- **ORM**: Dapper (lightweight, performant)

### AI/ML
- ** AWS Bedrock**

---

## 🎯 Design Decisions

### 1. Data Access Layer (DAL) Pattern

**Decision**: Implement a dedicated Data Access Layer that abstracts all database operations.

**Rationale**:
- ✅ **Database Agnostic**: Can switch from PostgreSQL to MySQL, DynamoDB, or any other database with minimal changes
- ✅ **Separation of Concerns**: Business logic separated from data persistence
- ✅ **Testability**: Easy to mock DAL for unit testing
- ✅ **Maintainability**: Single place for all database queries
- ✅ **Consistency**: All Lambdas use the same data access patterns

**Alternative Considered**: Direct database calls in each Lambda

**Why Not**: Creates tight coupling and makes database migration difficult

---

### 2. Application-Level Events (SNS/SQS) vs Database Triggers

**Decision**: Use SNS/SQS for event-driven summarization instead of PostgreSQL triggers.

**Rationale**:
- ✅ **Database Portability**: Not locked into PostgreSQL-specific features
- ✅ **Visibility**: All business logic visible in application code, not hidden in database
- ✅ **Testability**: Easier to test and debug application code vs triggers
- ✅ **Flexibility**: Can easily add more event subscribers (notifications, analytics, etc.)
- ✅ **Aligns with Team**: Shows complex integration patterns for Complex Integrations Team

**Alternative Considered**: PostgreSQL AFTER INSERT/UPDATE triggers

**Why Not**: Creates vendor lock-in and makes logic harder to test and maintain

**Trade-offs Acknowledged**:
- PostgreSQL triggers would eliminate race conditions more elegantly
- Application-level approach requires careful idempotent design
- Decision prioritizes long-term maintainability over short-term simplicity

---

### 3. Handling Race Conditions

**Problem**: Async summarization could cause race conditions where summary updates before initial save completes.

**Solution**: Multi-layered approach
1. **Ordered Publishing**: SNS event only published AFTER successful database save
2. **DAL Returns Change Detection**: UpsertNoteAsync returns whether content changed
3. **Smart Publishing**: Only publish to SNS if content actually changed
4. **Idempotent Updates**: Summary update uses `WHERE summary IS NULL OR summary = ''`

**Implementation**:
```csharp
public class UpsertResult
{
    public Note SavedNote { get; set; }
    public bool ContentChanged { get; set; }
}

// Only publish if content changed
if (result.ContentChanged)
{
    await PublishToSNS(result.SavedNote);
}
```

This prevents:
- ❌ Publishing events for unchanged content
- ❌ Race conditions between save and summarize
- ❌ Duplicate summarization work
- ❌ Infinite loops from updates triggering more updates

---

### 4. Idempotency Design

**Decision**: Make all operations idempotent at multiple levels.

**Implementation**:

**At Database Level**:
```sql
UPDATE notes 
SET summary = @summary, updated_at = NOW()
WHERE id = @noteId 
  AND (summary IS NULL OR summary = '')
-- Only updates if summary doesn't exist yet
```

**At Lambda Level**:
- SQS retries are safe - duplicate messages won't cause duplicate summaries
- DELETE is naturally idempotent
- UPSERT uses INSERT...ON CONFLICT

**Rationale**: Distributed systems require idempotency for reliability

---

### 5. Minimal Coupling in Summarize Lambda

**Decision**: Summarize Lambda doesn't read from database to check if summary exists.

**Rationale**:
- ✅ **Trust Boundary**: Summarize trusts that Upsert made the right decision to publish
- ✅ **Minimal Coupling**: Summarize only needs write operations, not read
- ✅ **Performance**: Avoids extra database round-trip
- ✅ **Simpler Interface**: Cleaner contract between services

**Alternative Considered**: Summarize Lambda checks if note already has summary

**Why Not**: Creates unnecessary coupling to database schema and duplicates logic

---

### 6. CloudFormation Template Included

**Decision**: Provide complete CloudFormation template for one-command deployment.

**Resources Defined**:
- Lambda Functions (Upsert, List/Fetch, Delete, Summarize)
- API Gateway with REST endpoints
- SNS Topic for note events
- SQS Queue with Dead Letter Queue
- IAM Roles and Policies
- RDS PostgreSQL instance (or connection to Docker PostgreSQL)
- VPC configuration for Lambda-to-RDS connectivity

**Deployment**: `aws cloudformation create-stack --template-file template.yaml`

---
### 7. Single Lambda for CRUD vs Multiple Lambdas

**Decision**: Use one Lambda function to handle all CRUD operations instead of separate Lambdas.

**Rationale**:
- ✅ **Appropriate Scale**: For a "tiny notes service", one Lambda is sufficient
- ✅ **Simpler Deployment**: Single function to build, test, and deploy
- ✅ **Reduced Cold Starts**: One warm Lambda vs multiple separate ones
- ✅ **Easier Maintenance**: All CRUD logic in one place
- ✅ **Lower Costs**: Fewer Lambda invocations and executions

**When to Split**: 
If this service grew to handle millions of requests with different scaling 
patterns (e.g., 90% reads, 10% writes), then splitting into separate Lambdas 
would make sense for independent scaling.

**For Now**: Single Lambda with route handling is the right balance of 
simplicity and demonstration of AWS Lambda capabilities.

---

## 🚀 Setup Instructions

### Prerequisites
- Docker & Docker Compose
- .NET 8 SDK
- Node.js 18+ and npm/yarn
- AWS CLI configured (for deployment)
- OpenAI API key (or AWS Bedrock access)

### Local Development

#### 1. Clone Repository
```bash
git clone [your-repo-url]
cd notes-service
```

#### 2. Start PostgreSQL Database
```bash
cd infrastructure
docker-compose up -d
```

This starts PostgreSQL on `localhost:5432` with:
- Database: `notesdb`
- Username: `notesuser`
- Password: `notespass`

#### 3. Run Database Migrations
```bash
cd backend
dotnet ef database update
# Or run SQL scripts in /database/migrations/
```

#### 4. Configure Backend
```bash
cd backend
cp appsettings.example.json appsettings.Development.json
# Edit connection strings and API keys
```

**Required Configuration**:
```json
{
  "ConnectionStrings": {
    "PostgreSQL": "Host=localhost;Port=5432;Database=notesdb;Username=notesuser;Password=notespass"
  },
  "AWS": {
    "Region": "us-east-1",
    "SNSTopicArn": "arn:aws:sns:region:account:note-events"
  },
  "OpenAI": {
    "ApiKey": "your-api-key",
    "Model": "gpt-4"
  }
}
```

#### 5. Run Backend
```bash
cd backend
dotnet run
# API available at https://localhost:5001
```

#### 6. Run Frontend
```bash
cd frontend
npm install
npm start
# App available at http://localhost:3000
```

### AWS Deployment

#### Using CloudFormation
```bash
aws cloudformation create-stack \
  --stack-name notes-service \
  --template-body file://cloudformation-template.yaml \
  --parameters ParameterKey=DatabasePassword,ParameterValue=YourSecurePassword \
  --capabilities CAPABILITY_IAM
```

---

## 📚 API Documentation

### Base URL
- **Local**: `http://localhost:5001/api`
- **AWS**: `https://[api-id].execute-api.[region].amazonaws.com/prod/api`

### Endpoints

#### Create Note
```http
POST /notes
Content-Type: application/json

{
  "id": "00000000-0000-0000-0000-000000000000",
  "content": "This is my note content"
}

Response: 200 OK
{
  "id": "550e8400-e29b-41d4-a716-446655440000",
  "content": "This is my note content",
  "summary": null,
  "createdAt": "2025-10-17T10:30:00Z",
  "updatedAt": "2025-10-17T10:30:00Z"
}
```

#### Update Note
```http
PUT /notes/{id}
Content-Type: application/json

{
  "content": "Updated note content"
}

Response: 200 OK
```

#### Get All Notes (with Search & Sort)
```http
GET /notes?search=keyword&sortBy=createdAt&sortOrder=desc

Response: 200 OK
[
  {
    "id": "550e8400-e29b-41d4-a716-446655440000",
    "content": "Note content",
    "summary": "AI-generated summary",
    "createdAt": "2025-10-17T10:30:00Z",
    "updatedAt": "2025-10-17T10:30:00Z"
  }
]
```

**Query Parameters**:
- `search` (optional): Search term for full-text search
- `sortBy` (optional): Field to sort by (`createdAt`, `updatedAt`, `content`)
- `sortOrder` (optional): `asc` or `desc` (default: `desc`)

#### Get Single Note
```http
GET /notes/{id}

Response: 200 OK
{
  "id": "550e8400-e29b-41d4-a716-446655440000",
  "content": "Note content",
  "summary": "AI-generated summary",
  "createdAt": "2025-10-17T10:30:00Z",
  "updatedAt": "2025-10-17T10:30:00Z"
}
```

#### Delete Note
```http
DELETE /notes/{id}

Response: 204 No Content
```

---

## 📁 Project Structure

```
notes-service/
├── frontend/                      # React/Angular frontend
│   ├── src/
│   │   ├── components/
│   │   │   ├── NoteList.tsx
│   │   │   ├── NoteForm.tsx
│   │   │   └── SearchBar.tsx
│   │   ├── services/
│   │   │   └── api.ts
│   │   ├── App.tsx
│   │   └── index.tsx
│   └── package.json
│
├── backend/                       # .NET 8/9 Backend
│   ├── src/
│   │   ├── API/                  # API Gateway / Web API
│   │   │   ├── Controllers/
│   │   │   └── Program.cs
│   │   │
│   │   ├── Lambdas/              # AWS Lambda Functions
│   │   │   ├── UpsertLambda/
│   │   │   │   └── Function.cs
│   │   │   ├── ListFetchLambda/
│   │   │   │   └── Function.cs
│   │   │   ├── DeleteLambda/
│   │   │   │   └── Function.cs
│   │   │   └── SummarizeLambda/
│   │   │       └── Function.cs
│   │   │
│   │   ├── DAL/                  # Data Access Layer
│   │   │   ├── INotesDAL.cs
│   │   │   ├── NotesDAL.cs
│   │   │   └── Models/
│   │   │       ├── Note.cs
│   │   │       └── UpsertResult.cs
│   │   │
│   │   ├── Services/             # Business Logic
│   │   │   ├── IAISummarizationService.cs
│   │   │   └── OpenAISummarizationService.cs
│   │   │
│   │   └── Infrastructure/       # Cross-cutting concerns
│   │       ├── ConnectionFactory.cs
│   │       └── Configuration/
│   │
│   └── tests/                    # Unit & Integration Tests
│       ├── DAL.Tests/
│       └── Lambda.Tests/
│
├── infrastructure/               # IaC & Database
│   ├── cloudformation-template.yaml
│   ├── cdk/                     # AWS CDK (optional)
│   │   ├── lib/
│   │   └── bin/
│   ├── docker-compose.yml       # PostgreSQL container
│   └── database/
│       ├── migrations/
│       └── seed-data.sql
│
├── docs/
│   ├── architecture-diagram.png
│   └── api-documentation.md
│
└── README.md
```

---

## 🧪 Testing

### Unit Tests
```bash
cd backend/tests
dotnet test
```

### Integration Tests
```bash
# Start test database
docker-compose -f docker-compose.test.yml up -d

# Run integration tests
dotnet test --filter Category=Integration
```

### Test Coverage
- DAL methods with in-memory database
- Lambda handler logic with mocked dependencies
- Search and sort functionality
- Idempotent operations

---

## 🔐 Security Considerations

1. **API Authentication**: Add JWT or API Key authentication (not implemented for assignment scope)
2. **Input Validation**: All inputs validated and sanitized
3. **SQL Injection Prevention**: Parameterized queries via Dapper
4. **Secrets Management**: Use AWS Secrets Manager for API keys (not hardcoded)
5. **CORS**: Properly configured for frontend domain
6. **Rate Limiting**: API Gateway throttling configured

---

## 📊 Monitoring & Observability

### CloudWatch Metrics
- Lambda invocation count, duration, errors
- SQS queue depth and message age
- API Gateway 4xx/5xx errors

### Logging
- Structured logging in all Lambdas
- Correlation IDs for request tracing
- Log aggregation in CloudWatch Logs

### Alerts
- Dead Letter Queue messages (failed summarizations)
- Lambda error rate > 5%
- API Gateway latency > 3s

---

