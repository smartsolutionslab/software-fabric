import { contentToText, type Message } from '@ag-ui/client';
import { z } from 'zod';
import { nextPlacement, type Placement } from './layout';

/** What propose_widget answers: a declared widget the user may see, as a visual its kind can show, under its title. */
const proposalSchema = z.object({
  source: z.string(),
  title: z.string(),
  visual: z.string(),
  /** The key of the draft's widget it takes the place of, or null for a new widget. */
  replaces: z.string().nullable(),
});

export type WidgetProposal = z.infer<typeof proposalSchema>;

/** A proposal the user has neither accepted nor discarded yet, by the tool call that made it. */
export interface PendingProposal {
  call: string;
  proposal: WidgetProposal;
}

/**
 * The proposal a propose_widget result holds, or none. The result is the model's turn and the host's answer, so it is
 * read as untrusted input: a refusal is text, and anything not shaped like a proposal is none.
 */
export function readProposal(content: string): WidgetProposal | undefined {
  let value: unknown;
  try {
    value = JSON.parse(content);
  } catch {
    return undefined;
  }

  const read = proposalSchema.safeParse(value);
  return read.success ? read.data : undefined;
}

/** Each tool call's result in the conversation, by the call's id. */
export const toolResults = (messages: readonly Message[]): ReadonlyMap<string, string> =>
  new Map(
    messages.flatMap((message) =>
      message.role === 'tool' ? [[message.toolCallId, contentToText(message.content)] as const] : [],
    ),
  );

/** Every widget the agent proposed, in order, by its call; a refused call proposes nothing. */
export function proposalsOf(messages: readonly Message[], results: ReadonlyMap<string, string>): PendingProposal[] {
  const proposals: PendingProposal[] = [];
  for (const message of messages) {
    if (message.role !== 'assistant') {
      continue;
    }

    for (const call of message.toolCalls ?? []) {
      const result = results.get(call.id);
      const proposal =
        call.function.name === 'propose_widget' && result !== undefined ? readProposal(result) : undefined;
      if (proposal) {
        proposals.push({ call: call.id, proposal });
      }
    }
  }

  return proposals;
}

/** What a tool call did, as the conversation shows it: a proposal by its declared title, a refusal by the host's reason. */
export function toolOutcome(tool: string, result: string | undefined): string {
  if (tool !== 'propose_widget') {
    return result === undefined ? `Running ${tool}…` : `Ran ${tool}.`;
  }

  if (result === undefined) {
    return 'Proposing a widget…';
  }

  const proposal = readProposal(result);
  return proposal
    ? `Proposed: ${proposal.title} as ${proposal.visual}.`
    : `Refused: ${result.replace(/^Failed: /, '')}`;
}

/** The draft's widget the proposal replaces, if the draft still has it. */
export const replacedBy = (placements: readonly Placement[], proposal: WidgetProposal): Placement | undefined =>
  proposal.replaces === null ? undefined : placements.find((placement) => placement.key === proposal.replaces);

/**
 * The draft with the proposal accepted: in the place of the widget it replaces, or below everything as a new widget,
 * as one added from the picker. Nothing is saved; Save does that.
 */
export function acceptProposal(placements: readonly Placement[], proposal: WidgetProposal, key: string): Placement[] {
  const { source, visual } = proposal;
  const replaced = replacedBy(placements, proposal);
  if (replaced) {
    return placements.map((placement) => (placement === replaced ? { ...placement, key, source, visual } : placement));
  }

  return [...placements, nextPlacement(placements, key, source, visual)];
}

/**
 * Where the pending proposals show on the grid: one below the other under the draft's widgets, where each would go as a
 * new widget, fixed there until it is decided.
 */
export function pendingOnGrid(placements: readonly Placement[], pending: readonly PendingProposal[]) {
  const above: Placement[] = [...placements];
  return pending.map((proposed) => {
    const { call, proposal } = proposed;
    const place = nextPlacement(above, call, proposal.source, proposal.visual);
    above.push(place);
    return {
      proposed,
      item: { i: `proposed-${call}`, x: place.x, y: place.y, w: place.width, h: place.height, static: true },
    };
  });
}

/** The run's AG-UI state: the open draft's widgets by key, source and visual, which the agent reads as context. */
export const draftState = (placements: readonly Placement[]) => ({
  widgets: placements.map(({ key, source, visual }) => ({ key, source, visual })),
});
