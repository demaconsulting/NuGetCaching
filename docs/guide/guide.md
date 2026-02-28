# Introduction

## Purpose

This document is the user guide for DemaConsulting NuGet Caching, a library for caching
NuGet packages on the local PC.

## Scope

This user guide covers:

- Installation of the library
- Basic usage and examples
- API reference

# Installation

Install the library using the .NET CLI:

```bash
dotnet add package DemaConsulting.NuGet.Caching
```

# Usage

## Basic Usage

```csharp
using DemaConsulting.NuGet.Caching;

var demo = new DemoClass();
var result = demo.DemoMethod("World");
Console.WriteLine(result); // Output: Hello, World!
```

## API Reference

> **Note**: `DemoClass` is placeholder functionality included in the current release.
> Additional NuGet package caching APIs will be added in future releases.

### DemoClass

The `DemoClass` provides demonstration functionality until the NuGet caching features
are implemented.

#### Constructors

##### DemoClass()

```csharp
public DemoClass()
```

Initializes a new instance of the `DemoClass` class with the default prefix "Hello".

##### DemoClass(string prefix)

```csharp
public DemoClass(string prefix)
```

Initializes a new instance of the `DemoClass` class with a custom prefix.

**Parameters:**

- `prefix` (string): The prefix to use in greetings. Must not be null.

**Exceptions:**

- `ArgumentNullException`: Thrown when `prefix` is null.

#### Methods

##### DemoMethod

```csharp
public string DemoMethod(string name)
```

Returns a greeting message for the specified name.

**Parameters:**

- `name` (string): The name to greet. Must not be null.

**Returns:**

A string containing the greeting message in the format "{prefix}, {name}!".

**Exceptions:**

- `ArgumentNullException`: Thrown when `name` is null.

**Example:**

```csharp
var demo = new DemoClass();
var greeting = demo.DemoMethod("World");
// greeting = "Hello, World!"
```

# Examples

## Example 1: Basic Greeting

```csharp
using DemaConsulting.NuGet.Caching;

var demo = new DemoClass();
var result = demo.DemoMethod("Alice");
Console.WriteLine(result);
// Output: Hello, Alice!
```

## Example 2: Custom Prefix

```csharp
using DemaConsulting.NuGet.Caching;

var demo = new DemoClass("Hi");
var result = demo.DemoMethod("Alice");
Console.WriteLine(result);
// Output: Hi, Alice!
```

## Example 3: Error Handling

```csharp
using DemaConsulting.NuGet.Caching;

var demo = new DemoClass();
try
{
    var result = demo.DemoMethod(null);
}
catch (ArgumentNullException ex)
{
    Console.WriteLine("Name cannot be null");
}
```
