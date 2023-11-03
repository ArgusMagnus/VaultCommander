using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VaultCommander.Commands;

interface IArgumentsTitle { public string? Title { get; } }
interface IArgumentsUsername { public string? Username { get; } }
interface IArgumentsPassword { public string? Password { get; } }
interface IArgumentsTotp { public string? Totp { get; } }
interface IArgumentsHost { public string? Host { get; } }