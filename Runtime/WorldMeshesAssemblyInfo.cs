using System.Runtime.CompilerServices;

/*
 * WorldMeshes runtime authoring data intentionally keeps mutation and
 * validation helpers internal so they are not exposed as part of the
 * public runtime API.
 *
 * Unity compiles scripts under an Editor folder into the predefined
 * Assembly-CSharp-Editor assembly, while Runtime scripts compile into
 * Assembly-CSharp. Stage 11 editor validation/signature code needs
 * controlled access to those internal helpers.
 */
[assembly: InternalsVisibleTo("Assembly-CSharp-Editor")]
