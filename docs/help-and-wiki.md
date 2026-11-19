# Help & Wiki and the help assistant

Every signed-in person has:

- **Help & Wiki** (`/help`, last item in the menu): a plain-words guide to every screen they can open,
  searchable, with popular tasks and a filter for "For me / Managers / HR & admins".
- **Help for this screen**: the **?** in the header opens the guide for the screen on show, beside it.
- **The help assistant** ("Ask for help", bottom right): ready questions and typed ones.

Help follows the menu. A person only sees help for the screens in their menu (permissions, plan
modules, employee record), and the assistant never answers about a screen they cannot open. People
with `admin.setting.view` or `admin.user.view` can tick "Show every screen" to help others.

## Where things live

| What | Where |
|---|---|
| Guides, one-line descriptions, ready questions, popular tasks | `frontend/libs/core/layout/src/lib/help/help-content.ts` (loaded on demand) |
| Rules: help pages from the menu, search, picking an answer | `help-engine.ts` (pure, unit-tested in `help-engine.spec.ts`) |
| Page, panel, assistant | `help-page.ts`, `help-panel.ts`, `help-assistant.ts`, `help-article.ts`, `help-center.ts` |
| Notes and feedback API | `backend/Modules/Platform/Gggs.Modules.Platform/Endpoints/HelpEndpoints.cs` |

## Writing a guide

- Write for the person using the screen, in plain words. Name things exactly as the screen does and
  wrap them in `[[ ]]` (for example `Press [[Apply for leave]].`); they show as chips.
- Every screen in the menu needs a one-line description in `SUMMARIES`; a full guide in `GUIDES` is
  added for the screens people use most. A unit test fails when a menu screen has no description, or
  when a guide, related link or ready question points at a screen that no longer exists.
- Change a screen, change its guide in the same pull request.

## The assistant

No AI: it matches words against the ready questions and the help pages, so it costs nothing, works
on-premise and never sees personal data. Where the person may read them, it also searches the
organisation's Helpdesk knowledge base and the policies that apply to them, and says where an answer
came from. With no answer it offers **Raise a helpdesk ticket** (My tickets opens with the question
filled in) or the Help & Wiki search.

## Organisation notes

Administrators with `admin.setting.manage` can add a short "In our company" note to any help page
(for example "our leave year starts in April"). Notes are stored as tenant settings (module `help`,
key = help page id such as `me.leave`), read by everyone in the organisation through
`GET /api/v1/help/notes`, and saved with `PUT /api/v1/help/notes/{topicId}`. An emptied note is hidden.

## Feedback

`POST /api/v1/help/feedback` logs (event id **7301**, category `Gggs.Help`) unanswered questions and
answers or pages marked helpful or not. Filter the logs on that event to decide which guides to write
or improve next. The platform console has the same feature with its own guides (repository
GGGS-ERP-PLATFORM, event id 7302).
