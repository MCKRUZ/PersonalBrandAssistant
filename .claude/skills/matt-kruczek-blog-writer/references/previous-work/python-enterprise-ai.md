# From Patterns to Production: Implementing Multi-Agent Systems in the Microsoft Stack

If you've been following the AI agent conversation, you've probably seen Google's recent article on multi-agent patterns in their Agent Development Kit (ADK). The patterns themselves are solid, they represent real architectural solutions to complex orchestration problems. But here's the thing: if you're building in the Microsoft ecosystem, you don't need ADK to implement these patterns. The Microsoft Agent Framework, released in October 2025, provides native support for every pattern Google describes, often with better tooling and deeper integration into enterprise workflows.

This article translates those patterns into working Python code using Microsoft Agent Framework. More importantly, it explains what each pattern actually solves and when you should reach for it.

## Why Multi-Agent Architecture Matters

Before diving into patterns, let's establish why you'd split work across multiple agents instead of building one powerful agent that handles everything.

Single agents hit three fundamental constraints:

**Context Limitations**: Even with large context windows, cramming all your business logic, domain knowledge, and task-specific instructions into one agent creates confusion. The agent struggles to determine which capabilities apply to which situations.

**Specialization vs. Generalization**: A generalist agent that tries to handle legal review, technical analysis, and customer communication will underperform specialists in each domain. The instructions for each capability dilute the effectiveness of the others.

**Cognitive Load**: Complex tasks benefit from breaking down into subtasks, just like human teams don't assign one person to handle every aspect of a project.

Multi-agent systems solve these problems by creating specialized agents, each optimized for specific capabilities, then orchestrating them into workflows that deliver cohesive outcomes.

## Microsoft Agent Framework: The Foundation

The Microsoft Agent Framework emerged from the merger of Semantic Kernel and AutoGen, two pioneering projects in enterprise AI orchestration. It provides production-grade capabilities for building agent systems:

- **Graph-based workflows** with conditional routing and parallel execution
- **Thread-based state management** for long-running conversations
- **Built-in observability** through OpenTelemetry integration
- **Type-safe message passing** between agents
- **Checkpointing** for resumable workflows
- **Native Azure integration** for enterprise deployment

The framework supports both .NET and Python with consistent APIs. For this article, we're focusing on Python implementations that leverage the framework's full capabilities.

## Pattern 1: Sequential Pipeline - The Assembly Line

**What It Is**: Agents execute in order, each building on the previous agent's output. Think of it as an assembly line where each station adds value before passing work to the next.

**When To Use It**:
- Content creation workflows (draft, edit, review, format)
- Data processing pipelines (extract, transform, validate, load)
- Document workflows (research, write, review, approve)
- Any task that naturally decomposes into ordered stages

**Why It Helps**: Sequential pipelines create predictable, traceable workflows. Each agent has one job and does it well. When something goes wrong, you know exactly which stage failed. It's the simplest multi-agent pattern and often the right starting point.

**Real-World Example**: A technical documentation pipeline where one agent generates initial content based on code analysis, a second agent ensures technical accuracy, a third agent improves readability, and a fourth formats for publication.

### Implementation in Python

```python
import asyncio
from agent_framework.azure import AzureOpenAIClient
from agent_framework import WorkflowBuilder
from azure.identity import DefaultAzureCredential

async def main():
    # Initialize Azure OpenAI client
    client = AzureOpenAIClient(credential=DefaultAzureCredential())
    
    # Create specialized agents
    researcher = client.create_agent(
        name="Researcher",
        instructions="""You are a research agent. Gather comprehensive information 
            about the topic and provide detailed findings with sources."""
    )
    
    writer = client.create_agent(
        name="Writer",
        instructions="""You are a technical writer. Transform research findings 
            into clear, well-structured content suitable for documentation."""
    )
    
    reviewer = client.create_agent(
        name="Reviewer",
        instructions="""You are an editor. Review content for clarity, accuracy, 
            and consistency. Provide actionable feedback."""
    )
    
    # Build sequential workflow
    workflow = (WorkflowBuilder()
        .set_start_executor(researcher)
        .add_edge(researcher, writer)
        .add_edge(writer, reviewer)
        .build())
    
    # Execute the workflow
    result = await workflow.run(
        "Create documentation for implementing OAuth 2.0 authentication"
    )
    
    print(f"Final Output:\n{result.text}")

if __name__ == "__main__":
    asyncio.run(main())
```

**Key Advantages**:
- Each agent specializes in one task
- Easy to debug (know exactly which stage failed)
- Straightforward to add or remove stages
- Results are traceable through the pipeline

## Pattern 2: Generator-Critic Loop - Iterative Refinement

**What It Is**: One agent generates content, another evaluates it and provides feedback, and the loop continues until quality criteria are met or iteration limits are reached.

**When To Use It**:
- Quality-critical outputs (code, legal documents, financial analysis)
- Creative work requiring refinement (marketing copy, product descriptions)
- Problem-solving that benefits from multiple perspectives
- Any scenario where first attempts rarely meet standards

**Why It Helps**: This pattern embodies the principle that criticism improves creation. The generator focuses on producing output without self-censoring, while the critic provides objective evaluation. The separation of roles produces better outcomes than asking one agent to both create and critique.

**Real-World Example**: A code generation system where one agent writes implementation, another identifies bugs and edge cases, and they iterate until the code passes all criteria (tests, security scans, performance benchmarks).

### Implementation in Python

```python
import asyncio
from agent_framework.azure import AzureOpenAIClient
from agent_framework import WorkflowBuilder
from azure.identity import DefaultAzureCredential

async def main():
    client = AzureOpenAIClient(credential=DefaultAzureCredential())
    
    # Create generator and critic agents
    generator = client.create_agent(
        name="CodeGenerator",
        instructions="""You are an expert software developer. Generate clean, 
            efficient code based on specifications. Accept feedback and iterate."""
    )
    
    critic = client.create_agent(
        name="CodeReviewer",
        instructions="""You are a senior code reviewer. Analyze code for bugs, 
            security issues, performance problems, and best practices. 
            If issues exist, respond with 'NEEDS_REVISION: [detailed feedback]'. 
            If code meets standards, respond with 'APPROVED'."""
    )
    
    # Build feedback loop with conditional edge
    workflow = (WorkflowBuilder()
        .set_start_executor(generator)
        .add_edge(generator, critic)
        .add_edge(
            critic, 
            generator,
            condition=lambda response: "NEEDS_REVISION" in str(response)
        )
        .build())
    
    # Execute with iteration tracking
    max_iterations = 5
    iteration = 0
    
    async for event in workflow.run_stream(
        "Write a function to process payment transactions with retry logic"
    ):
        if event.type == "agent_update":
            print(f"[Iteration {iteration}] {event.executor_id}: {event.data}")
            if event.executor_id == "CodeReviewer":
                iteration += 1
                if iteration >= max_iterations:
                    print("Max iterations reached. Stopping.")
                    break
        elif event.type == "workflow_output":
            print(f"\nFinal Code:\n{event.data}")

if __name__ == "__main__":
    asyncio.run(main())
```

**Key Advantages**:
- Objective evaluation separate from generation
- Iterative improvement without manual intervention
- Quality gates enforced programmatically
- Agents can specialize in either creation or evaluation

## Pattern 3: Parallel Execution - Divide and Conquer

**What It Is**: Multiple agents work simultaneously on different aspects of the same problem, then results are combined.

**When To Use It**:
- Independent subtasks that don't depend on each other
- Time-sensitive operations where speed matters
- Scenarios requiring multiple perspectives (competitive analysis, risk assessment)
- When you need to maximize throughput

**Why It Helps**: Parallel execution dramatically reduces total processing time. Instead of waiting for each agent sequentially, multiple agents work concurrently. This pattern is particularly valuable when each subtask takes significant time.

**Real-World Example**: A competitive intelligence system where separate agents simultaneously analyze different competitors (pricing, features, marketing, technology stack), then a synthesis agent combines findings into a unified report.

### Implementation in Python

```python
import asyncio
from agent_framework.azure import AzureOpenAIClient
from agent_framework import WorkflowBuilder, RouterExecutor
from azure.identity import DefaultAzureCredential

async def main():
    client = AzureOpenAIClient(credential=DefaultAzureCredential())
    
    # Create specialized analysis agents
    pricing_agent = client.create_agent(
        name="PricingAnalyst",
        instructions="Analyze competitor pricing strategies and models."
    )
    
    feature_agent = client.create_agent(
        name="FeatureAnalyst",
        instructions="Analyze competitor product features and capabilities."
    )
    
    marketing_agent = client.create_agent(
        name="MarketingAnalyst",
        instructions="Analyze competitor marketing and positioning strategies."
    )
    
    synthesis_agent = client.create_agent(
        name="Synthesizer",
        instructions="""Combine analysis from multiple perspectives into a 
            comprehensive competitive intelligence report."""
    )
    
    # Create router to distribute work
    router = RouterExecutor(
        "Router",
        targets=[pricing_agent, feature_agent, marketing_agent]
    )
    
    # Build parallel workflow
    workflow = (WorkflowBuilder()
        .set_start_executor(router)
        .add_edge(pricing_agent, synthesis_agent)
        .add_edge(feature_agent, synthesis_agent)
        .add_edge(marketing_agent, synthesis_agent)
        .build())
    
    # Execute parallel analysis
    async for event in workflow.run_stream(
        "Analyze the competitive landscape for Microsoft 365 alternatives"
    ):
        if event.type == "agent_update":
            print(f"{event.executor_id}: {event.data}")
        elif event.type == "workflow_output":
            print(f"\nSynthesized Report:\n{event.data}")

if __name__ == "__main__":
    asyncio.run(main())
```

**Key Advantages**:
- Significant time savings through concurrent execution
- Multiple specialized perspectives on the same problem
- Scales naturally (add more parallel agents as needed)
- Each agent focuses on its domain without distraction

## Pattern 4: Orchestrator-Workers - Hierarchical Delegation

**What It Is**: A coordinator agent receives requests, breaks them into subtasks, delegates to specialized worker agents, and synthesizes results.

**When To Use It**:
- Complex requests requiring multiple capabilities
- Dynamic workflows where the path depends on the request
- Situations requiring intelligent task decomposition
- When worker selection needs to be contextual

**Why It Helps**: This pattern mimics how effective teams work in organizations. The orchestrator understands the big picture and knows which specialists to engage. Workers focus solely on their domain expertise. This separation enables sophisticated coordination without workers needing to understand the full workflow.

**Real-World Example**: A customer support system where an orchestrator agent triages requests, then routes to specialists (account issues, technical problems, billing questions, feature requests), and synthesizes responses into coherent customer communication.

### Implementation in Python

```python
import asyncio
from agent_framework.azure import AzureOpenAIClient
from azure.identity import DefaultAzureCredential

async def main():
    client = AzureOpenAIClient(credential=DefaultAzureCredential())
    
    # Create specialized worker agents
    technical_agent = client.create_agent(
        name="TechnicalSupport",
        instructions="Resolve technical issues with software products."
    )
    
    billing_agent = client.create_agent(
        name="BillingSupport",
        instructions="Handle billing questions and payment issues."
    )
    
    account_agent = client.create_agent(
        name="AccountSupport",
        instructions="Assist with account management and security."
    )
    
    # Create orchestrator with worker agents as tools
    async def handle_technical(issue: str) -> str:
        """Handle technical product issues"""
        result = await technical_agent.run(issue)
        return result.text
    
    async def handle_billing(issue: str) -> str:
        """Handle billing and payment questions"""
        result = await billing_agent.run(issue)
        return result.text
    
    async def handle_account(issue: str) -> str:
        """Handle account management requests"""
        result = await account_agent.run(issue)
        return result.text
    
    orchestrator = client.create_agent(
        name="SupportOrchestrator",
        instructions="""You are a customer support coordinator. 
            Analyze customer requests and delegate to appropriate specialists:
            - Technical issues -> handle_technical
            - Billing questions -> handle_billing
            - Account problems -> handle_account
            Synthesize specialist responses into clear customer communication.""",
        tools=[handle_technical, handle_billing, handle_account]
    )
    
    # Execute orchestration
    result = await orchestrator.run(
        "I can't access my account and was charged twice this month"
    )
    
    print(f"Response to customer:\n{result.text}")

if __name__ == "__main__":
    asyncio.run(main())
```

**Key Advantages**:
- Dynamic workflow paths based on request content
- Workers remain focused on specific domains
- Orchestrator handles complexity of coordination
- Easy to add new workers without changing existing ones

## Pattern 5: Evaluator-Optimizer - Continuous Improvement

**What It Is**: An evaluator agent measures performance against criteria, an optimizer agent adjusts parameters or strategies, creating a feedback loop that drives improvement.

**When To Use It**:
- A/B testing and optimization scenarios
- Systems that need to adapt to changing conditions
- Scenarios with measurable success criteria
- Long-running systems that should improve over time

**Why It Helps**: This pattern embodies continuous improvement. Rather than static optimization, the system adapts based on actual results. The evaluator provides objective measurement while the optimizer focuses on strategy adjustment.

**Real-World Example**: A marketing campaign system where an evaluator measures engagement metrics (click-through rates, conversions, cost-per-acquisition), and an optimizer adjusts messaging, targeting, or creative elements to improve performance.

### Implementation in Python

```python
import asyncio
import json
from dataclasses import dataclass, asdict
from agent_framework.azure import AzureOpenAIClient
from agent_framework import WorkflowBuilder
from azure.identity import DefaultAzureCredential

@dataclass
class PerformanceMetrics:
    click_through_rate: float
    conversion_rate: float
    cost_per_acquisition: float
    current_strategy: str

async def main():
    client = AzureOpenAIClient(credential=DefaultAzureCredential())
    
    # Create evaluator and optimizer agents
    evaluator = client.create_agent(
        name="PerformanceEvaluator",
        instructions="""Analyze campaign performance metrics. 
            If CTR < 2% or CPA > $50, respond with 'NEEDS_OPTIMIZATION: [analysis]'.
            Otherwise respond with 'PERFORMING_WELL: [analysis]'."""
    )
    
    optimizer = client.create_agent(
        name="CampaignOptimizer",
        instructions="""Based on performance analysis, recommend specific 
            optimizations to improve metrics. Provide actionable changes to 
            messaging, targeting, or creative elements."""
    )
    
    # Build evaluation-optimization loop
    workflow = (WorkflowBuilder()
        .set_start_executor(evaluator)
        .add_edge(
            evaluator,
            optimizer,
            condition=lambda response: "NEEDS_OPTIMIZATION" in str(response)
        )
        .build())
    
    # Simulate optimization cycles
    current_metrics = PerformanceMetrics(
        click_through_rate=1.5,
        conversion_rate=3.2,
        cost_per_acquisition=65.0,
        current_strategy="Broad targeting with generic messaging"
    )
    
    for cycle in range(3):
        print(f"\n=== Optimization Cycle {cycle + 1} ===")
        print(f"Current Metrics: {json.dumps(asdict(current_metrics))}")
        
        async for event in workflow.run_stream(
            f"Evaluate these campaign metrics: {json.dumps(asdict(current_metrics))}"
        ):
            if event.type == "agent_update":
                print(f"{event.executor_id}: {event.data}")
            elif event.type == "workflow_output":
                print(f"Recommendation: {event.data}")
        
        # Simulate metric improvements
        current_metrics.click_through_rate *= 1.2
        current_metrics.cost_per_acquisition *= 0.9

if __name__ == "__main__":
    asyncio.run(main())
```

**Key Advantages**:
- Continuous improvement without manual intervention
- Objective measurement drives decisions
- System adapts to changing conditions
- Clear separation between evaluation and optimization logic

## Pattern 6: Human-in-the-Loop - Governance and Oversight

**What It Is**: Workflows pause at critical decision points for human review and approval before proceeding.

**When To Use It**:
- High-stakes decisions (financial transactions, legal actions, personnel decisions)
- Regulatory compliance requirements
- Learning scenarios where humans train the system
- Situations requiring judgment AI shouldn't make alone

**Why It Helps**: Not every decision should be fully automated. Human-in-the-loop patterns provide governance without sacrificing the efficiency of AI automation. Humans review only critical decisions, while agents handle routine processing.

**Real-World Example**: A procurement system where agents process routine purchases autonomously but escalate high-value or unusual purchases to procurement managers for approval.

### Implementation in Python

```python
import asyncio
from agent_framework.azure import AzureOpenAIClient
from agent_framework import WorkflowBuilder
from azure.identity import DefaultAzureCredential

async def main():
    client = AzureOpenAIClient(credential=DefaultAzureCredential())
    
    # Create agents for purchase workflow
    purchase_agent = client.create_agent(
        name="PurchaseProcessor",
        instructions="""Process purchase requests. Calculate total cost and 
            determine if approval is needed (>$10,000 or unusual items)."""
    )
    
    approval_agent = client.create_agent(
        name="ApprovalProcessor",
        instructions="""Process approved purchases. Generate purchase orders 
            and update financial systems."""
    )
    
    # Build workflow with approval gate
    workflow = (WorkflowBuilder()
        .set_start_executor(purchase_agent)
        .add_edge(
            purchase_agent,
            approval_agent,
            condition=lambda response: "APPROVED" in str(response) or "AUTO_APPROVE" in str(response)
        )
        .build())
    
    # Execute with human approval
    purchase_analysis = None
    
    async for event in workflow.run_stream(
        "Purchase request: 50 laptops at $2,500 each = $125,000"
    ):
        if event.type == "agent_update" and event.executor_id == "PurchaseProcessor":
            purchase_analysis = str(event.data)
            print(f"Agent Analysis:\n{purchase_analysis}\n")
            
            if "REQUIRES_APPROVAL" in purchase_analysis:
                # Pause for human decision
                print("=== HUMAN APPROVAL REQUIRED ===")
                decision = input("Approve this purchase? (yes/no): ")
                
                if decision.lower() == "yes":
                    # Continue workflow
                    result = await approval_agent.run("APPROVED by manager. Proceed.")
                    print(f"\nFinal Result:\n{result.text}")
                else:
                    print("Purchase rejected by manager.")
                    break
        elif event.type == "workflow_output":
            print(f"\nFinal Result:\n{event.data}")

if __name__ == "__main__":
    asyncio.run(main())
```

**Key Advantages**:
- Humans make critical decisions, AI handles routine work
- Compliance requirements satisfied through documented approvals
- Training opportunity (human decisions teach the system)
- Risk mitigation for high-stakes scenarios

## Why Microsoft Agent Framework Over Google ADK

The patterns we've covered are conceptually similar between frameworks, but Microsoft Agent Framework provides several advantages for enterprise deployment:

**1. Enterprise Integration**
Native Azure integration means security, compliance, and governance are built-in, not bolted on. The framework works seamlessly with Azure OpenAI, Azure AI Studio, and Microsoft 365.

**2. Production-Ready Observability**
OpenTelemetry integration provides distributed tracing across multi-agent workflows. You can visualize agent interactions, measure performance, and debug issues using tools like Azure Monitor and the Aspire Dashboard.

**3. Type Safety**
Python's type hints combined with the framework's validation catch errors before runtime. Message contracts between agents are validated, preventing failures from incompatible data structures.

**4. Thread-Based State Management**
Workflows can persist state through checkpoints and resume from interruptions. This is critical for long-running processes and human-in-the-loop scenarios.

**5. Flexible Deployment**
Run workflows locally during development, deploy to Azure AI Foundry for production, or host in your own infrastructure. The same code works across all environments.

## Workflow State Management

For long-running workflows or human-in-the-loop scenarios, state persistence is critical:

```python
# Execute with state persistence
run = await workflow.run("Process this complex task")

# Save state at checkpoint
checkpoint = await run.save_checkpoint()

# Later, resume from checkpoint
resumed_run = await workflow.resume(checkpoint)
```

Threads serialize all conversation state, allowing workflows to pause for hours or days (awaiting human approval) and resume exactly where they left off.

## Observability and Debugging

Microsoft Agent Framework includes OpenTelemetry instrumentation by default:

```python
from opentelemetry import trace
from opentelemetry.sdk.trace import TracerProvider
from opentelemetry.sdk.trace.export import ConsoleSpanExporter, BatchSpanProcessor
from azure.monitor.opentelemetry.exporter import AzureMonitorTraceExporter

# Configure tracing
trace.set_tracer_provider(TracerProvider())
trace.get_tracer_provider().add_span_processor(
    BatchSpanProcessor(ConsoleSpanExporter())
)
trace.get_tracer_provider().add_span_processor(
    BatchSpanProcessor(AzureMonitorTraceExporter(
        connection_string=os.environ["APPLICATIONINSIGHTS_CONNECTION_STRING"]
    ))
)
```

This provides distributed traces showing agent execution timing, message flow, tool invocations, and errors with full context.

## Deployment Options

**Local Development**: Run workflows directly in your development environment with full debugging.

**Azure AI Foundry Agent Service**: Deploy agents to managed infrastructure with enterprise security, scaling, and monitoring.

**Custom Hosting**: Host workflows in your own infrastructure using FastAPI or Flask:

```python
from fastapi import FastAPI
from agent_framework.azure import AzureOpenAIClient
from azure.identity import DefaultAzureCredential

app = FastAPI()
client = AzureOpenAIClient(credential=DefaultAzureCredential())

agent = client.create_agent(
    name="Assistant",
    instructions="You are a helpful assistant."
)

@app.post("/api/workflow")
async def run_workflow(input: str):
    result = await agent.run(input)
    return {"response": result.text}
```

## Implementation Tips

**Agent Instructions Matter**: Be specific about role, output format, and decision criteria. Generic instructions produce generic results.

**Conditional Edges Need Structure**: Use clear keywords or JSON in agent outputs for reliable conditional routing.

**Iteration Limits Prevent Loops**: Always include maximum iteration counts for feedback loops.

**Type Hints Catch Issues**: Use type hints for message contracts between agents.

## Key Metrics to Track

**Performance**: End-to-end workflow time, individual agent execution time, token usage, concurrent capacity

**Quality**: Task success rate, human intervention frequency, iteration counts, error rates

**Cost**: Token consumption per agent, API costs per workflow, infrastructure costs

**Operations**: Failure rate, recovery time, checkpoint success rate, storage usage

## Getting Started

1. **Install Prerequisites**
   ```bash
   pip install agent-framework --pre
   pip install agent-framework-azure-ai --pre
   ```

2. **Authenticate**
   ```bash
   az login
   ```

3. **Start Simple**: Begin with sequential pipeline (Pattern 1)

4. **Add Complexity Gradually**: Add conditional routing, then parallel execution

5. **Instrument Early**: Add observability from the start

The multi-agent patterns we've covered represent real architectural solutions to complex orchestration problems. Microsoft Agent Framework provides production-ready tooling to implement these patterns in enterprise environments. The framework's integration with Azure and built-in observability make it a compelling choice for organizations building agent systems in the Microsoft ecosystem.

---

*Matthew Kruczek is Managing Director at EY, leading Microsoft domain initiatives within Digital Engineering. This article is part of "The Agent-First Enterprise" series exploring how organizations can transform their operations around AI agent capabilities. Connect with Matthew on LinkedIn to discuss multi-agent implementation strategies for your organization.*

## References

1. Google Developers Blog. "A Developer's Guide to Multi-Agent Patterns in ADK."
2. Microsoft Learn. "Introduction to Microsoft Agent Framework."
3. Microsoft Learn. "Microsoft Agent Framework Workflows."
4. Microsoft Developer Blog. "Introducing Microsoft Agent Framework (Preview)." November 2025.
