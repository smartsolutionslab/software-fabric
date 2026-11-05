## Embeddings: Azure OpenAI

- Embeddings come from a deployment in your Azure OpenAI resource, through Microsoft's `Microsoft.Extensions.AI.OpenAI` adapter calling `<endpoint>/openai/v1/`. Only `src/BuildingBlocks/Llm/Wiring/AzureOpenAIEmbeddingGenerator.cs` names the provider; every other piece of code takes `IEmbeddingGenerator<string, Embedding<float>>`.
- The endpoint and the key are the AppHost's parameters `azure-openai-endpoint` and `azure-openai-api-key` (a secret); never write the key anywhere in the repository, a log or a test.
- The deployment is the AppHost's parameter `azure-openai-embedding-deployment`, `text-embedding-3-small` (1536 dimensions) by default. Changing it to a model of another size is a decision: stored vectors must be made again, in tables of the new size. Never hard-code a deployment name in a module.
- Tests never call the model: they stand in for `IEmbeddingGenerator`.
