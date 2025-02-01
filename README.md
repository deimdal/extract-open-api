# extract-open-api

A `dotnet tool` for reducing OpenApi specification documents by filtering out unnecessary operations.  
Schema v2 and v3 are supported
---

## Usage

1. Specify source document (URL or file name)
2. Specify destination document file name
3. Specify path filter (what path should be preserved) with optional operation filter (see below)
4. Specify optional parameters (if required): destination document version and format

> Schema filter is not supported. Schema tree-shaking will be performed for preserved path automatically

## Path and operation filter

> Please, ensure your command shell allows symbols like `{` and `}`, or use escaped strings otherwise

**Path filter** can be:

- Single `-p /pet/{petId}/uploadImage`
- Multiple `-p /pet/{petId}/uploadImage /pet/findByStatus /user/{username} /pet/findByTags`

Path filter can contain **operation filter**:

- Single operation `-p /user/{username}=get` (only GET operation for this path will be preserved in the final document)
- Multiple operations `-p /user/{username}=get,put` (GET and PUT operations for this path will be preserved in the final document)

## Installation (global tool)

```shell
dotnet tool install Deimdal.ExtractOpenApi --global
```

## Usage command

```shell
extract-open-api -s <source OpenApi file or URL> -d <destination OpenApi file> -p <path filter 1> <path filter 2> <...>
```

## Examples
Get specification from URL `https://petstore.swagger.io/v2/swagger.json`, preserve all operations for path `/user/{username}` and save result to file `spec.yaml` in YAML format
```shell
extract-open-api -s https://petstore.swagger.io/v2/swagger.json -d spec.yaml -p /user/{username}
```
Get specification from URL `https://petstore.swagger.io/v2/swagger.json`, preserve all operations for path `/store/inventory` and GET operation for path `/pet/{petId}`, then save result to file `spec.yaml` in YAML format
```shell
extract-open-api -s https://petstore.swagger.io/v2/swagger.json -d spec.yaml -p /store/inventory /pet/{petId}=get
```
Get specification from URL `https://petstore.swagger.io/v2/swagger.json`, preserve all operations for path `/pet`, `/pet/{petId}` and `/store/inventory`, then save result to file `spec.json` in JSON format
```shell
extract-open-api -s https://petstore.swagger.io/v2/swagger.json -d spec.json -f json -p /pet /pet/{petId} /store/inventory
```


## See help for full parameters usage details

```shell
extract-open-api -h
```
