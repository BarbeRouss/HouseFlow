# Architecture

Architecture diagrams of HouseFlow and of its agent team.

- [`hf-dispatch-states.md`](hf-dispatch-states.md): state diagrams (Mermaid, rendered by GitHub) of a PR and of an
  issue in the agent workflow, with Julien's commands, cross-cutting rules and known defects.
- [`hf-dispatch-flow.bpmn`](hf-dispatch-flow.bpmn): BPMN 2.0 process, one lane per actor. Open and edit it with
  [bpmn.io](https://demo.bpmn.io) (drop the file), the bpmn-io VS Code extension ("BPMN Editor") or Camunda Modeler.

Conventions: Mermaid for state diagrams, bpmn.io for BPMN, everything in English. Both files first describe the
current state; the target version corrected by Julien then becomes the specification for the `hf-dispatch` tests.
