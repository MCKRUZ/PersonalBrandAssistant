# Progressive Disclosure for MCP Servers: A Design Pattern for Scalable AI Tool Integration

**A Technical Whitepaper for Enterprise Technology Leaders**

*Matthew Kruczek, Managing Director, EY Digital Engineering*

---

## Executive Summary

The Model Context Protocol has become the de facto standard for connecting AI agents to external tools and data. As adoption accelerates, a scaling problem has emerged that threatens the viability of MCP for enterprise deployments: tool definitions consume massive amounts of context window space before any actual work begins.

The numbers are stark. A typical enterprise MCP server with 400 tools consumes over 400,000 tokens just loading tool schemas. Claude's maximum context window is 200,000 tokens. The math doesn't work.

Progressive disclosure solves this problem by revealing tool complexity gradually rather than all at once. Instead of loading every tool definition upfront, agents discover tools on demand, fetching full schemas only when needed. Production implementations report 85-100x reductions in token usage while maintaining or improving tool selection accuracy.

This whitepaper formalizes progressive disclosure as an architectural pattern for MCP servers. It establishes a taxonomy of approaches currently in production, provides design principles for implementation, and offers practical guidance for enterprise adoption. The pattern isn't optional at scale. It's required.

---

## The Problem: Context Window Bloat and Accuracy Degradation

### The Token Economics of Tool Definitions

Every MCP tool requires a definition that includes its name, description, and input schema. A well-documented tool with proper parameter descriptions typically consumes 500-1,500 tokens. Multiply that across dozens or hundreds of tools, and the context window fills before the user asks their first question.

Speakeasy's benchmarks tell the story clearly:

| Toolset Size | Initial Token Consumption |
|--------------|--------------------------|
| 40 tools | 43,300 tokens |
| 100 tools | 128,900 tokens |
| 200 tools | 261,700 tokens |
| 400 tools | 405,100 tokens |

At 200 tools, you've exceeded Claude's context window. At 400 tools, you've doubled it. And these numbers assume a single MCP server. Enterprise deployments typically connect agents to multiple servers simultaneously.

### The Cognitive Overload Effect

Token consumption is only half the problem. Research published in late 2024 (arXiv:2411.15399, "Less is More: Optimizing Function Calling for LLM Execution on Edge Devices") found that LLM decision-making degrades significantly when presented with too many tools. The authors identified a threshold around 20-25 tools where accuracy begins to decline measurably.

The mechanism is intuitive if you think about it. When a model must evaluate hundreds of tool options for every request, it spends cognitive capacity on tool selection that could otherwise go toward understanding the user's intent and reasoning about the problem. More choices don't help when most of them are irrelevant.

Anthropic's internal testing reinforces this finding. When they implemented lazy tool loading for Claude, Opus 4 improved from 49% to 74% accuracy on tool selection benchmarks. Opus 4.5 jumped from 79.5% to 88.1%. The improvement came not from better models but from showing the model fewer, more relevant tools.

### The Compound Problem

Enterprise deployments make this worse. A developer using Claude Code might connect to:

- A GitHub MCP server (repository operations, issues, pull requests)
- A Jira server (project management, tickets, workflows)
- A Slack server (messaging, channels, search)
- A database server (queries, schema inspection)
- Internal tooling servers specific to their organization

Each server contributes its own tool definitions. The context window fills. Accuracy drops. Latency increases. Costs climb.

The industry needs a better approach.

---

## Taxonomy of Progressive Disclosure Patterns

Five distinct patterns have emerged for implementing progressive disclosure in MCP servers. They share a common principle (defer loading until needed) but differ in how they structure discovery.

### The Two-Stage Pattern

The simplest progressive disclosure implementation separates tool listing from schema retrieval. A project from the MCP Birthday Hackathon demonstrated this approach with two mechanisms:

**Stage 1: Minimal Listing.** The `tools/list` endpoint returns ultra-minimal descriptions:

```json
{
  "name": "aws_ec2_launch_instance",
  "description": "Launches a new AWS EC2 instance with specified configuration.",
  "inputSchema": {"type": "object", "properties": {}}
}
```

Note the empty `inputSchema`. The agent knows the tool exists and what it does at a high level, but the actual parameters remain hidden.

**Stage 2: On-Demand Schema Retrieval.** When the agent decides to use a tool, it fetches the full definition via a resource:

```
resource:///tool_descriptions?tools=aws_ec2_launch_instance
```

This returns the complete schema with all parameters, validation rules, and examples.

The hackathon implementation reported 96% token reduction in typical workflows. The trade-off is an additional round trip before tool execution, but for most use cases, the latency is negligible compared to the token savings.

### The Strata Pattern

Klavis AI's Strata server (YC X25) extends two-stage discovery into a four-stage funnel:

**Stage 1: Intent Recognition.** The agent expresses what it wants to accomplish in natural language. The server interprets intent without exposing any tool details.

**Stage 2: Category Navigation.** Based on intent, the server presents relevant categories (e.g., "Email Operations," "CRM Management," "File Storage"). The agent selects which category to explore.

**Stage 3: Action Name Discovery.** Within the selected category, the agent sees available action names with brief descriptions. Still no schemas.

**Stage 4: Full Schema Retrieval.** Only when the agent commits to a specific action does it receive the complete input schema.

This approach works particularly well for large, heterogeneous toolsets. Klavis reports +13-15% accuracy improvement over standard MCP implementations and 83%+ success rates on complex workflows.

The funnel structure means an agent connecting to 500 tools might see only 5-10 categories initially, then 10-20 actions within a category, then a single schema. Context consumption stays bounded regardless of total toolset size.

### The Gram Pattern

Speakeasy's Gram implementation takes a different approach: embeddings-based search over tool descriptions.

```typescript
// Agent describes intent in natural language
const relevantTools = await findTools({
  query: "I need to update a customer record in Salesforce"
});

// Returns: [{name: "salesforce_update_record", similarity: 0.94}, ...]
```

The agent never sees a hierarchical structure. It describes what it wants, and the system returns relevant tools ranked by semantic similarity.

**Advantages:**
- Fastest discovery path (single query vs. multi-step navigation)
- Natural language interface matches how humans think about tasks
- No need to maintain category hierarchies

**Trade-offs:**
- Less complete visibility into available tools
- Quality depends heavily on tool description quality
- May miss relevant tools if descriptions don't match query phrasing

Speakeasy's benchmarks show semantic search using approximately 30% fewer tokens than progressive hierarchical search for simple tasks. For complex tasks requiring multiple tools, the difference narrows.

### The Tree Pattern

Several open-source implementations (lazy-mcp, OpenMCP) organize tools into navigable tree structures:

```
servers/
â”œâ”€â”€ hubspot/
â”‚   â”œâ”€â”€ contacts/
â”‚   â”‚   â”œâ”€â”€ create.ts
â”‚   â”‚   â”œâ”€â”€ update.ts
â”‚   â”‚   â””â”€â”€ search.ts
â”‚   â””â”€â”€ deals/
â”‚       â”œâ”€â”€ create.ts
â”‚       â””â”€â”€ list.ts
â””â”€â”€ salesforce/
    â””â”€â”€ records/
        â”œâ”€â”€ query.ts
        â””â”€â”€ update.ts
```

The agent explores this tree using path-based queries:

```typescript
// List available servers
list_tools("/")  // Returns: ["hubspot", "salesforce"]

// Drill into HubSpot
list_tools("/hubspot/")  // Returns: ["contacts", "deals"]

// Get tools in contacts
list_tools("/hubspot/contacts/")  // Returns tool summaries

// Fetch specific schema
describe_tool("/hubspot/contacts/create")  // Returns full schema
```

This pattern mirrors how developers naturally organize code. Agents familiar with filesystem navigation adapt quickly. The hierarchical structure also provides implicit categorization without requiring explicit metadata.

### The Skills Pattern

Claude's Agent Skills framework, combined with Anthropic's code-as-tools approach, implements progressive disclosure at a higher level of abstraction. Rather than exposing individual tools directly, it layers organizational knowledge on top of tool capabilities through a three-level architecture.

**Level 1: Metadata (loaded at startup)**

```yaml
name: azure-infrastructure
description: Provisions and manages Azure resources following organizational standards
```

This consumes roughly 100 tokens per skill. An agent can have dozens of skills available while using minimal context.

**Level 2: Instructions (loaded when skill activates)**

When a user's request matches a skill's domain, the agent loads the full SKILL.md file:

```markdown
# Azure Infrastructure Skill

## When to Use This Skill
Use this skill when the user needs to provision, modify, or inspect Azure resources.

## Required Context
- Subscription ID (ask user if not provided)
- Resource group naming convention: {project}-{environment}-rg

## Procedures

### Provisioning a New Resource Group
1. Verify the user has specified environment (dev/staging/prod)
2. Generate name following convention
3. Execute: az group create --name {name} --location eastus2
...
```

Best practice keeps SKILL.md under 5,000 tokens. The skill defines procedures, constraints, and organizational knowledge without exposing raw tool schemas.

**Level 3: Resources (loaded on demand)**

Skills can include scripts, reference documents, and configuration files that load only when the agent determines they're needed:

```
azure-infrastructure/
â”œâ”€â”€ SKILL.md
â”œâ”€â”€ scripts/
â”‚   â”œâ”€â”€ provision-rg.sh
â”‚   â””â”€â”€ validate-naming.py
â””â”€â”€ references/
    â”œâ”€â”€ naming-conventions.md
    â””â”€â”€ approved-regions.json
```

Scripts execute without loading into context. Reference documents load only when explicitly needed. This means the knowledge a skill can encapsulate is effectively unlimited while context consumption remains bounded.

**Code-as-Tools Extension**

Anthropic's engineering team extended this pattern by representing tools as code files on a filesystem:

```typescript
// ./servers/google-drive/getDocument.ts
import { callMCPTool } from "../../../client.js";

interface GetDocumentInput {
  documentId: string;
}

interface GetDocumentResponse {
  content: string;
}

/* Read a document from Google Drive */
export async function getDocument(
  input: GetDocumentInput
): Promise<GetDocumentResponse> {
  return callMCPTool<GetDocumentResponse>('google_drive__get_document', input);
}
```

The agent discovers tools by navigating the filesystem, reading only the definitions it needs. This approach adds several capabilities beyond basic progressive disclosure:

**Composability.** Agents can write code that chains multiple tools together, executing complex workflows in a single code block rather than through sequential tool calls:

```typescript
// Read transcript from Google Docs and add to Salesforce prospect
import * as gdrive from './servers/google-drive';
import * as salesforce from './servers/salesforce';

const transcript = (await gdrive.getDocument({ documentId: 'abc123' })).content;
await salesforce.updateRecord({
  objectType: 'SalesMeeting',
  recordId: '00Q5f000001abcXYZ',
  data: { Notes: transcript }
});
```

**Intermediate Result Filtering.** Results stay in the execution environment. The agent can filter, transform, or aggregate data before returning it to the context window:

```typescript
const allRows = await gdrive.getSheet({ sheetId: 'abc123' });
const pendingOrders = allRows.filter(row => row["Status"] === 'pending');
console.log(`Found ${pendingOrders.length} pending orders`);
// Only filtered results enter context, not all 10,000 rows
```

**State Persistence.** The execution environment maintains state across operations. Agents can write intermediate results to files, save reusable functions as new skills, and pick up where they left off.

**Privacy Preservation.** Sensitive data can flow through workflows without ever entering the model's context window. Customer records move from spreadsheet to CRM without the model seeing actual values.

Anthropic reports this combined approach reduced their benchmark from 150,000 tokens to 2,000 tokens for equivalent functionality. That's a 98.7% reduction.

The Skills Pattern solves two problems simultaneously: it implements progressive disclosure for token efficiency, and it packages organizational knowledge that generic models lack. This combination makes it particularly powerful for enterprise deployments where both scaling and customization matter.

---

## Design Principles for Implementation

Regardless of which pattern you choose, five principles should guide your implementation.

### Principle 1: Minimize Initial Footprint

The context window is a shared resource. Every token consumed by tool definitions is a token unavailable for the user's actual request, the model's reasoning, and the conversation history.

Target less than 100 tokens per tool for initial context. This means:

- Name: keep it descriptive but concise
- Description: one sentence maximum
- Schema: defer entirely or provide empty placeholder

A server with 100 tools should consume under 10,000 tokens at startup. That leaves room for actual work.

### Principle 2: Enable Progressive Depth

Structure your implementation with clearly separated levels:

**Index Level:** What tools exist? Names and brief descriptions only.

**Detail Level:** What does this tool need? Full input schemas with parameter documentation.

**Deep Level:** How exactly does this work? Examples, edge cases, error handling, related tools.

Each level should be independently addressable. An agent should be able to fetch index information without triggering detail loading. It should be able to get details for a specific tool without loading the entire catalog.

### Principle 3: Preserve Discovery Completeness

Progressive disclosure should never hide tools permanently. An agent exploring systematically must be able to find any tool in the system.

This matters particularly for semantic search implementations. If your search embeddings don't capture a tool well, agents may never discover it. Consider hybrid approaches: semantic search for common queries with fallback to structured navigation for comprehensive discovery.

Document what's available at each level. Make the discovery mechanism itself discoverable.

### Principle 4: Optimize for Common Cases

Most tasks require 3-5 tools from potentially hundreds available. Design for this reality.

Cache recently used tool schemas. If an agent used `salesforce.updateRecord` in the last three interactions, that schema is likely relevant again. Keep it warm.

Implement related tool suggestions. When an agent fetches the schema for `contacts.create`, preemptively indicate that `contacts.update` and `contacts.search` exist. Many workflows involve predictable tool combinations.

Consider task-based bundles for common workflows. Rather than discovering tools individually, offer preset collections for standard operations.

### Principle 5: Separate Discovery from Execution

Discovery operations should be read-only and cheap. An agent should be able to explore the tool catalog speculatively without cost or side effects.

This means:

- Listing tools should never execute anything
- Fetching schemas should never modify state
- Search queries should never consume API quotas on underlying services

Keep discovery operations fast enough that agents can explore freely. If checking whether a tool exists takes 500ms, agents will avoid exploratory discovery.

---

## Reference Architecture: Bridging Skills and MCP Servers

Claude's Agent Skills framework and MCP servers solve related problems with different approaches. Skills package organizational knowledge; MCP servers expose external capabilities. Progressive disclosure works for both.

Here's a reference architecture that unifies them:

```
agent-workspace/
â”œâ”€â”€ skills/
â”‚   â”œâ”€â”€ azure-infrastructure/
â”‚   â”‚   â”œâ”€â”€ SKILL.md
â”‚   â”‚   â””â”€â”€ scripts/
â”‚   â””â”€â”€ quarterly-reporting/
â”‚       â”œâ”€â”€ SKILL.md
â”‚       â””â”€â”€ templates/
â””â”€â”€ servers/
    â”œâ”€â”€ index.json          # Minimal server metadata
    â”œâ”€â”€ github/
    â”‚   â”œâ”€â”€ manifest.json   # Tool names + descriptions
    â”‚   â””â”€â”€ schemas/        # Full schemas, loaded on demand
    â””â”€â”€ salesforce/
        â”œâ”€â”€ manifest.json
        â””â”€â”€ schemas/
```

The `index.json` at the root lists available servers with brief descriptions:

```json
{
  "servers": [
    {"name": "github", "description": "Repository and issue management"},
    {"name": "salesforce", "description": "CRM operations and reporting"}
  ]
}
```

Each server's `manifest.json` lists tools without schemas:

```json
{
  "tools": [
    {"name": "createIssue", "description": "Creates a new GitHub issue"},
    {"name": "listPullRequests", "description": "Lists PRs with optional filters"}
  ]
}
```

Full schemas live in separate files, loaded only when needed:

```json
// schemas/createIssue.json
{
  "name": "createIssue",
  "description": "Creates a new GitHub issue in the specified repository",
  "inputSchema": {
    "type": "object",
    "required": ["owner", "repo", "title"],
    "properties": {
      "owner": {"type": "string", "description": "Repository owner"},
      "repo": {"type": "string", "description": "Repository name"},
      "title": {"type": "string", "description": "Issue title"},
      "body": {"type": "string", "description": "Issue body (markdown)"},
      "labels": {"type": "array", "items": {"type": "string"}}
    }
  }
}
```

Skills can reference MCP servers in their instructions:

```markdown
# Code Review Skill

## Tools Required
This skill uses the `github` MCP server for repository operations.

## Procedure
1. Fetch the PR diff using `github.getPullRequest`
2. Analyze changes against team standards (see references/standards.md)
3. Post review comments using `github.createReviewComment`
```

The skill provides procedural knowledge; the MCP server provides execution capability. Progressive disclosure keeps both context-efficient.

---

## When to Use Which Pattern

Different patterns suit different situations:

| Scenario | Recommended Pattern |
|----------|-------------------|
| Small toolset (<50 tools) | Two-Stage or static (if context allows) |
| Large single-domain API | Tree or Strata |
| Multiple heterogeneous servers | Skills with MCP servers beneath |
| Complex multi-tool workflows | Skills with code-as-tools extension |
| High-quality tool descriptions | Gram (semantic search) for fastest discovery |
| Organizational knowledge required | Skills with progressive resource loading |

Hybrid approaches often work best in practice. Use semantic search for initial discovery, fall back to hierarchical navigation when search results are ambiguous, and wrap everything in skills when organizational context matters.

---

## Implementation Guidance

### Start with Measurement

Before implementing progressive disclosure, measure your current state:

- How many tools does your agent connect to?
- What's the total token consumption at startup?
- What percentage of tools get used in a typical session?
- What's your current tool selection accuracy?

These baselines let you quantify improvement and justify investment.

### Migration Path

For existing MCP servers, a practical migration path:

1. **Separate manifests from schemas.** Restructure your server to serve tool listings and full schemas from different endpoints.

2. **Add discovery tools.** Implement `list_tools` and `describe_tool` (or equivalent) as meta-tools.

3. **Update clients.** Modify your MCP client to use progressive discovery rather than loading everything upfront.

4. **Monitor and tune.** Track which tools get discovered and used. Optimize descriptions for commonly needed tools.

### Testing Considerations

Token reduction is easy to measure. Accuracy is harder.

Build test suites that verify:

- Can the agent discover any tool given appropriate prompting?
- Does tool selection accuracy match or exceed baseline?
- Are multi-tool workflows still achievable?
- How does latency change with progressive discovery?

Measure accuracy alongside token savings. A 100x token reduction means nothing if the agent can't find the tools it needs.

---

## Conclusion

Progressive disclosure isn't a nice-to-have optimization. It's a required architectural pattern for MCP servers at scale.

The evidence is clear: static tool loading fails beyond a few dozen tools. Context windows overflow. Accuracy degrades. Costs explode. Every major implementation pushing MCP to enterprise scale has independently converged on progressive disclosure as the solution.

The patterns documented here represent current best practices drawn from production implementations at Anthropic, Klavis, Speakeasy, and the broader MCP community. They're not theoretical. They're working in production today.

For technology leaders evaluating MCP adoption, the question isn't whether to implement progressive disclosure but which pattern fits your use case. Start measuring your current token consumption. Identify which pattern aligns with your toolset structure and organizational needs. Build progressive discovery into your MCP strategy from the beginning.

The standard is emerging. The time to adopt is now.

---

*Matthew Kruczek is Managing Director at EY, leading Microsoft domain initiatives within Digital Engineering. He specializes in Azure, Generative AI, and Microsoft Copilot implementations. Connect with Matthew on LinkedIn to discuss progressive disclosure implementation strategies for your organization.*

---

## References

1. Anthropic. "Code execution with MCP: Building more efficient agents." November 4, 2025. https://www.anthropic.com/engineering/code-execution-with-mcp

2. Paramanayakam, V., Karatzas, A., Anagnostopoulos, I., & Stamoulis, D. "Less is More: Optimizing Function Calling for LLM Execution on Edge Devices." arXiv:2411.15399, November 2024.

3. Speakeasy. "Comparing Progressive Discovery and Semantic Search for Powering Dynamic MCP." November 13, 2025. https://www.speakeasy.com/blog/100x-token-reduction-dynamic-toolsets

4. Klavis AI. "Strata: One MCP server for AI agents to handle thousands of tools." Y Combinator X25. https://www.klavis.ai/

5. Martin, M. "MCP Extension Progressive Disclosure." MCP 1st Birthday Hackathon. https://huggingface.co/spaces/MCP-1st-Birthday/mcp-extension-progressive-disclosure

6. Anthropic. "Agent Skills Specification." 2025. https://agentskills.io/

7. Model Context Protocol Specification. November 2025. https://modelcontextprotocol.io/specification/2025-11-25

8. Patil, H. "Progressive Disclosure for Typed Library Discovery & Introspection." MCP SEP #1888, November 24, 2025. https://github.com/modelcontextprotocol/modelcontextprotocol/issues/1888
