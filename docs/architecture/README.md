# Architecture

Architecture diagrams of HouseFlow and of its agent team.

- [`hf-dispatch-states.md`](hf-dispatch-states.md): the workflow of the agent team (Mermaid, rendered by GitHub):
  issue lifecycle, labels and the transitions allowed per role (`hf-state`), comment routing, priority, delivery
  guarantees, Discord notifications, PR lifecycle with the review slot, dev work list, implementation status.
- [`hf-dispatch-data-model.md`](hf-dispatch-data-model.md): SQLite data model of hf-dispatch (Mermaid ER diagram).
- [`hf-dispatch-flow.bpmn`](hf-dispatch-flow.bpmn): BPMN 2.0 process, one lane per actor. **Outdated**: it still
  shows the workflow before 2026-11-23. Open and edit it with [bpmn.io](https://demo.bpmn.io), the bpmn-io VS Code
  extension ("BPMN Editor") or Camunda Modeler.

Conventions: Mermaid for state diagrams, bpmn.io for BPMN, everything in English.
