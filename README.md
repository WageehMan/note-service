# Notes Service

A full-stack serverless notes application with AI-powered summarization.

## 🔗 Quick Links

- **Live Demo:** http://notes-frontend-prod.s3-website-us-east-1.amazonaws.com
- **API Endpoint:** https://u1t4q70g86.execute-api.us-east-1.amazonaws.com/prod/notes
- **GitHub:** https://github.com/WageehMan/note-service

---

## ✨ Features

- **CRUD Operations**: Create, read, update, and delete notes
- **Search**: Full-text search across note content
- **AI Summarization**: Automatic summaries using AWS Bedrock
- **Asynchronous Processing**: Non-blocking summarization via SQS
- **Responsive UI**: Angular-based frontend

---

## 🏗 Architecture
```
                    ┌──────────────┐
                    │   Frontend   │
                    │   (Angular)  │
                    └──────┬───────┘
                           │
                           ▼
                    ┌──────────────┐
                    │ API Backend  │
                    │ (API Gateway)│
                    └──────┬───────┘
                           │
                    ┌──────┴───────┐
                    │   Post       │
                    │   Get        │
                    │   Delete     │
                    ▼              │
            ┌────────────────┐    │
            │   NOTE CRUD    │    │
            │    (Lambda)    │◄───┘
            └───────┬────────┘
                    │
                    ▼
            ┌────────────────┐
            │      DAL       │
            │ (Data Access)  │
            └───────┬────────┘
                    │
                    ▼
            ┌────────────────┐
            │  PostgreSQL    │
            │      (RDS)     │
            └────────────────┘

    After Successful Save:
    
    ┌────────────────┐
    │   NOTE CRUD    │
    │    (Lambda)    │
    └───────┬────────┘
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
    ┌────────────────┐
    │   Summarize    │
    │    (Lambda)    │
    └───────┬────────┘
            │
            ▼
    ┌────────────────┐
    │      DAL       │
    │    (Update)    │
    └───────┬────────┘
            │
            ▼
    ┌────────────────┐
    │  PostgreSQL    │
    │      (RDS)     │
    └────────────────┘
```

### Architecture Flow

1. **Frontend → API Backend**: User interacts with Angular application
2. **API Backend → NOTE CRUD Lambda**: Handles all CRUD operations (POST, GET, DELETE)
3. **Lambda → DAL → PostgreSQL**: All database operations abstracted through DAL
4. **Event-Driven Summarization** (after successful save):
   - NOTE CRUD Lambda publishes event to SNS
   - SQS receives message from SNS
   - Summarize Lambda processes message asynchronously
   - Summary updates note via DAL → PostgreSQL

---

## 🛠 Tech Stack

- **Frontend**: Angular 17, Standalone Components
- **Backend**: .NET 8, C#
- **AWS**: Lambda, API Gateway, RDS (PostgreSQL), SNS, SQS, Bedrock
- **Infrastructure**: CloudFormation, GitHub Actions
- **Database**: PostgreSQL with Dapper ORM

---

## 🚀 Setup & Deployment

### Prerequisites
- AWS Account
- GitHub Account
- .NET 8 SDK
- Node.js 18+

### 1. Set Up OIDC for GitHub Actions

**Create OIDC Provider in AWS:**
```bash
# Upload the CloudFormation template via AWS Console
# or use AWS CLI:
aws cloudformation create-stack \
  --stack-name github-oidc-setup \
  --template-body file://infrastructure/oidc-github-setup.yaml \
  --parameters ParameterKey=GitHubOrg,ParameterValue=WageehMan \
               ParameterKey=RepositoryName,ParameterValue=note-service \
  --capabilities CAPABILITY_NAMED_IAM \
  --region us-east-1
```

**Via AWS Console:**
1. Go to AWS CloudFormation console
2. Click **Create stack** → **With new resources**
3. Choose **Upload a template file**
4. Upload `infrastructure/oidc-github-setup.yaml`
5. Enter parameters:
   - **GitHubOrg**: `WageehMan`
   - **RepositoryName**: `note-service`
6. Check **I acknowledge that AWS CloudFormation might create IAM resources**
7. Click **Create stack**
8. Wait for `CREATE_COMPLETE` status

This creates:
- ✅ OIDC Identity Provider for GitHub
- ✅ IAM Role with deployment permissions
- ✅ Trust policy for your repository

### 2. Configure GitHub Secrets

Add these to your repository settings (`Settings` → `Secrets and variables` → `Actions`):
```
AWS_REGION=us-east-1
```

That's it! The workflow uses OIDC - no access keys needed.

### 3. Deploy Application

**Automatic Deployment:**
```bash
git add .
git commit -m "Deploy notes service"
git push origin main
```

GitHub Actions will automatically:
1. Build .NET Lambdas
2. Build Angular frontend
3. Deploy infrastructure via CloudFormation
4. Upload Lambda packages to S3
5. Deploy frontend to S3 bucket
6. Configure API Gateway

**Manual Deployment (if needed):**
```bash
# Deploy infrastructure
aws cloudformation create-stack \
  --stack-name NoteServiceStack \
  --template-body file://infrastructure/cloudformation-template.yaml \
  --parameters file://infrastructure/parameters.json \
  --capabilities CAPABILITY_NAMED_IAM
```

### 4. Local Development

**Start Database:**
```bash
docker-compose up -d
```

**Run Backend:**
```bash
cd NoteCrud
dotnet run
```

**Run Frontend:**
```bash
cd NotesService.Web
npm install
npm start
```

---

## 📚 API Documentation

**Base URL:** `https://u1t4q70g86.execute-api.us-east-1.amazonaws.com/prod`

### Endpoints

#### List All Notes
```http
GET /notes
```

#### Get Single Note
```http
GET /notes/{id}
```

#### Create Note
```http
POST /notes
Content-Type: application/json

{
  "content": "Your note content here"
}
```

#### Update Note
```http
PUT /notes/{id}
Content-Type: application/json

{
  "content": "Updated content"
}
```

#### Delete Note
```http
DELETE /notes/{id}
```

---

## 🎯 Key Design Decisions

### 1. Data Access Layer (DAL)
- Abstracts database operations between Lambdas and PostgreSQL
- Enables easy database switching
- Single source of truth for all queries
- Used by both NOTE CRUD and Summarize Lambdas

### 2. Event-Driven Summarization
- SNS/SQS for async processing after successful save
- Database agnostic (no triggers)
- Decouples summarization from main CRUD flow
- Easy to add more event subscribers

### 3. Idempotency
- Safe SQS retries
- Conditional summary updates (`WHERE summary IS NULL OR summary = ''`)
- No duplicate processing
- Race condition protection

### 4. Single Lambda for CRUD
- One Lambda handles POST, GET, and DELETE
- Simpler deployment and maintenance
- Lower costs for this scale
- Fewer cold starts

---

## 📁 Project Structure
```
note-service/
├── NotesService.Web/          # Angular frontend
├── NoteCrud/                  # Lambda: CRUD operations
├── Summarize/                 # Lambda: AI summarization
├── NoteService.DAL/           # Data access layer
└── infrastructure/
    ├── cloudformation-template.yaml
    ├── parameters.json
    ├── oidc-github-setup.yaml
    └── .github/workflows/
```

---

## 🔧 Environment Variables

The application uses these environment variables (configured in CloudFormation):
```bash
DATABASE_CONNECTION_STRING  # RDS PostgreSQL connection
AWS_REGION                 # AWS region (us-east-1)
SNS_TOPIC_ARN             # SNS topic for events
BEDROCK_MODEL_ID          # AWS Bedrock model
```

---

## 🧪 Testing
```bash
# Run all tests
dotnet test

# Run specific project
cd NoteService.DAL.Tests
dotnet test
```

---

## 📊 Monitoring

View logs and metrics in AWS CloudWatch:
- Lambda execution logs (NOTE CRUD & Summarize)
- API Gateway access logs
- SQS queue depth and message age
- Dead letter queue alerts

---

## 🔐 Security

- ✅ OIDC authentication (no long-lived credentials)
- ✅ Parameterized SQL queries (Dapper)
- ✅ IAM least-privilege roles
- ✅ VPC isolation for RDS
- ✅ CORS configured for frontend

---

## 📝 License

MIT

---

## 👤 Author

**Wageeh Man**
- GitHub: [@WageehMan](https://github.com/WageehMan)
- Repository: [note-service](https://github.com/WageehMan/note-service)