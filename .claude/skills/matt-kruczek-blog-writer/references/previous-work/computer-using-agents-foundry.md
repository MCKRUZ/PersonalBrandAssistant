# Computer-Using Agents in Microsoft Foundry: The End of RPA As We Know It?

**Category:** Enterprise AI | **Date:** February 9, 2026 | **Read time:** 8 min

---

## Executive read

*If you only have a minute, here's what you need to know.*

- Computer-Using Agents (CUA) are AI models that interact with software the way humans do: reading screens, clicking buttons, and navigating applications through natural language instructions.
- Microsoft now offers CUA through the Responses API in Microsoft Foundry. It's real and it works, but it's not ready to replace your RPA investments.
- The right move is hybrid: keep RPA for deterministic processes, deploy CUA where variability and judgment are required.
- Invest in governance infrastructure now so you can scale safely as the technology matures.
- Start with a sandboxed proof of concept targeting workflows where your current automation breaks most often.

## What CUA actually is (and isn't)

There's a new class of AI that doesn't just answer questions or generate content. It uses your computer. It clicks buttons, fills out forms, navigates between applications, and reasons its way through multi-step workflows. Microsoft calls it the Computer-Using Agent, or CUA, and it's now available in Microsoft Foundry through the Responses API.

At its core, CUA is an AI model within Azure OpenAI Service that interprets screenshots and decides what to click, type, or navigate next. You give it a natural language instruction like "go to our procurement system, find all invoices over $50,000 from last quarter, and export them to a spreadsheet." The model reasons through each step, takes a screenshot, figures out what's on screen, and acts.

This is different from traditional automation in a fundamental way. An RPA bot follows a predetermined script: click coordinates (412, 308), wait 2 seconds, type "Q4 invoices," press Enter. If someone moves that button 10 pixels to the right, or a popup appears that the script didn't anticipate, the bot crashes. CUA doesn't memorize pixel locations. It reads the interface the way you do, recognizing buttons by their labels and fields by their context. When a UI changes, it adapts.

But let's be honest about where things stand. On benchmarks that test full computer-use tasks, the best CUA models complete fewer than half the tasks human testers can. These numbers are improving quickly, roughly tripling in the first half of 2025, but "improving fast" and "production-ready for your finance department" are different statements.

## Why this isn't the death of RPA

I keep seeing headlines about CUA killing RPA, and that framing misses the point. RPA is a multi-billion dollar market with over half of enterprises running it in production. That installed base isn't going away.

I say this as someone who built a [Pluralsight course on RPA with Microsoft Power Automate](https://www.pluralsight.com/courses/rpa-power-automate-getting-started), walking developers through constructing workflows from scratch. I became intimately familiar with both what RPA does well and where it falls apart.

The limitations I ran into were consistent. Bots are brittle when UIs change. A vendor pushes a minor update, a button moves, a new dialog box appears, and your automation breaks at 2 AM with nobody watching. Date pickers are a recurring nightmare. So are dynamic forms where fields change depending on previous selections. And anytime you need the bot to handle an exception that wasn't in the original script, you're writing increasingly complex branching logic that becomes its own maintenance burden.

But for deterministic processes where the same steps happen the same way every time, RPA is still the better tool. It's faster, cheaper, and won't make a reasoning error on step 37 of a 50-step workflow. As the Microsoft Futures team wrote in October 2025: "One should only use agents to perform tasks that require reasoning."

Where CUA earns its place is precisely in those gaps I kept hitting while building RPA workflows. IT service desk tickets described in free text. Contractor onboarding across Workday, Okta, and Azure AD where each dialog changes based on role and region. I would have struggled to build reliable Power Automate flows for those scenarios. CUA handles them because it reasons about what to do next rather than following a fixed script.

The smart play is hybrid. UiPath and other vendors are already building CUA capabilities into their platforms, creating workflows that are mostly scripted with reasoning injected where things get unpredictable. Not replacement. Augmentation.

## How CUA fits into the Foundry ecosystem

Microsoft didn't release CUA as a standalone product. It's part of a layered architecture in Microsoft Foundry, and understanding those layers matters for planning.

The entry point is the Responses API (March 2025), a unified interface that lets you chain computer use, function calling, file search, and code interpretation in a single API call. Above that sits the Foundry Agent Service, which handles conversation management, tool orchestration, retries, logging, and compliance. It integrates with Semantic Kernel and AutoGen for multi-agent orchestration, meaning you can have a CUA agent working alongside API-based agents, each handling what they're best at. And the Foundry Control Plane (now in public preview) provides fleet-wide governance: task adherence guardrails, prompt injection detection, PII filtering, and tracing across every agent action.

Microsoft is also evaluating integration with Windows 365 and Azure Virtual Desktop, which would let CUA run in sandboxed Cloud PCs with only the applications and permissions the agent needs, governed through your existing Azure policies.

## Where the real enterprise value lives

I see three areas where CUA creates value that's hard to replicate with existing tools.

First, legacy system integration without APIs. Every large enterprise has 15- or 20-year-old systems that are still business-critical and offer no programmatic interface. CUA reads the screen, understands the interface semantically, and interacts with it. No API wrapper to build. No script to maintain when the vendor pushes an update.

Second, cross-application workflows with variability. An insurance claim that branches differently depending on claim type, coverage, and amount. Traditional RPA handles the happy path. CUA handles the edge cases.

Third, last-mile automation in knowledge work. Your analysts use Copilot to draft reports, but the deliverable still requires navigating systems, pulling dashboard data, pasting it into templates, and routing for approval. CUA bridges the gap between AI-assisted thinking and actual execution.

## The safety question you can't skip

Giving an AI agent the ability to click buttons in your production systems is a different risk profile than anything else in enterprise AI. A chatbot that hallucinates gives you a wrong answer. A CUA that hallucinates clicks the wrong button.

Microsoft has built safety at multiple layers: the model refuses harmful tasks and requests confirmation before irreversible actions, Foundry provides content filtering and execution monitoring, and the Control Plane adds real-time observability. But research presented at NeurIPS 2025 found that prompt injections still deceive top-tier CUA models in the vast majority of test cases. Current defenses don't catch everything.

Three principles for enterprise deployment: start in sandboxed environments with minimal permissions. Implement human-in-the-loop for anything consequential. And treat CUA access like a new employee's access: least privilege, monitored, with clear boundaries.

## Getting started

Audit your automation portfolio. Find the workflows where RPA bots fail most often or where you've abandoned automation because the process was too variable. Those are your CUA candidates. Build a proof of concept through the Responses API on something low-risk, run it sandboxed, and measure against the manual process. Don't rip out your RPA. Look for integration points where CUA handles the reasoning-intensive steps within broader automations that include deterministic RPA, API calls, and human checkpoints. And invest in governance before you scale.

The broader trajectory is clear: every legacy desktop application is becoming an intelligent API surface, whether it was designed to be one or not. CUA is the bridge. The organizations that build governance infrastructure and identify the right use cases now will move fastest when the capabilities catch up with the ambition. Based on what I'm watching, that's coming sooner than most people expect.

---

**References:**
- Microsoft Azure Blog, "Announcing the Responses API and Computer-Using Agent in Azure AI Foundry," March 2025
- Microsoft Tech Community, "The Future of AI: Computer Use Agents Have Arrived," October 2025
- OSWorld benchmark and OSWorld-Human efficiency study, June 2025
- NeurIPS 2025, WASP benchmark on CUA prompt injection vulnerability
