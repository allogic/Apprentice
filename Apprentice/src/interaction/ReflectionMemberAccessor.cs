using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;

namespace Apprentice
{
	/// <summary>
	/// Caches reflection member discovery used by interaction adapters.
	/// Member access itself remains guarded because game/mod implementations
	/// can expose getters or setters that throw for a particular state.
	/// </summary>
	internal static class ReflectionMemberAccessor
	{
		private const BindingFlags DeclaredInstanceFlags =
			BindingFlags.Instance |
			BindingFlags.Public |
			BindingFlags.NonPublic |
			BindingFlags.DeclaredOnly;

		private readonly record struct MemberKey(
			Type RuntimeType,
			string MemberName
		);

		private static readonly ConcurrentDictionary<
			MemberKey,
			MemberInfo[]
		> memberCache = new();
		private static readonly ConcurrentDictionary<
			Type,
			Type[]
		> typeHierarchyCache = new();

		public static void Clear()
		{
			memberCache.Clear();
			typeHierarchyCache.Clear();
		}

		public static object? GetValue(
			object? instance,
			params string[] memberNames)
		{
			if (instance == null)
			{
				return null;
			}

			foreach (Type currentType in GetTypeHierarchy(
				instance.GetType()))
			{
				foreach (string memberName in memberNames)
				{
					foreach (MemberInfo member in GetDeclaredMembers(
						currentType,
						memberName))
					{
						try
						{
							if (member is PropertyInfo property)
							{
								return property.GetValue(instance);
							}

							if (member is FieldInfo field)
							{
								return field.GetValue(instance);
							}
						}
						catch
						{
							// Preserve the adapters' previous fallback behavior.
						}
					}
				}
			}

			return null;
		}

		public static void WriteConverted(
			object instance,
			string memberName,
			object value)
		{
			ArgumentNullException.ThrowIfNull(instance);

			foreach (Type currentType in GetTypeHierarchy(
				instance.GetType()))
			{
				foreach (MemberInfo member in GetDeclaredMembers(
					currentType,
					memberName))
				{
					try
					{
						if (member is PropertyInfo property &&
							property.CanWrite)
						{
							property.SetValue(
								instance,
								Convert.ChangeType(
									value,
									property.PropertyType
								)
							);
							return;
						}

						if (member is FieldInfo field)
						{
							field.SetValue(
								instance,
								Convert.ChangeType(
									value,
									field.FieldType
								)
							);
							return;
						}
					}
					catch
					{
						// Try the next field/property or base type member.
					}
				}
			}
		}

		private static MemberInfo[] GetDeclaredMembers(
			Type declaringType,
			string memberName)
		{
			return memberCache.GetOrAdd(
				new MemberKey(declaringType, memberName),
				static key => ResolveDeclaredMembers(
					key.RuntimeType,
					key.MemberName
				)
			);
		}

		private static MemberInfo[] ResolveDeclaredMembers(
			Type declaringType,
			string memberName)
		{
			var members = new List<MemberInfo>();
			PropertyInfo? property = declaringType.GetProperty(
					memberName,
					DeclaredInstanceFlags
				);
			if (property != null &&
				property.GetIndexParameters().Length == 0)
			{
				members.Add(property);
			}

			FieldInfo? field = declaringType.GetField(
					memberName,
					DeclaredInstanceFlags
				);
			if (field != null)
			{
				members.Add(field);
			}

			return members.ToArray();
		}

		private static Type[] GetTypeHierarchy(Type runtimeType)
		{
			return typeHierarchyCache.GetOrAdd(
				runtimeType,
				static type =>
				{
					var types = new List<Type>();
					Type? currentType = type;
					while (currentType != null)
					{
						types.Add(currentType);
						currentType = currentType.BaseType;
					}

					return types.ToArray();
				}
			);
		}
	}
}
