## Module communication: contract interfaces

- To let other modules ask this module something now, add a method to `I<Module>Api` in `src/Contracts/<Module>/` and implement it in `<Module>Api` in the module's Api project by dispatching one of the module's own queries or commands; no business logic there.
- Parameters and results are Contracts or Kernel types, never a module's own; put the types an API needs beside it in `src/Contracts/<Module>/`.
- To ask another module, take its `I<Module>Api` in a handler's constructor. Never reference another module's projects; an architecture rule fails the build.
- Implement `I<Module>Api` only in its own module's Api project, once; an architecture rule fails a second implementation or one in another module. It is registered by convention; never register it by hand.
- A call does not join your transaction. To tell other modules that something changed, publish an integration event instead.
- Never change the signature of a method another module calls; add a new method beside it.
- A module's own test starts it alone, with other modules' APIs as stand-ins that throw if called; test a call across modules against the running host.
