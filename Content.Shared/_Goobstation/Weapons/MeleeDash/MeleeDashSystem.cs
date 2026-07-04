// SPDX-FileCopyrightText: 2025 Aiden <28298836+Aidenkrz@users.noreply.github.com>
// SPDX-FileCopyrightText: 2025 Aviu00 <93730715+Aviu00@users.noreply.github.com>
// SPDX-FileCopyrightText: 2025 Misandry <mary@thughunt.ing>
// SPDX-FileCopyrightText: 2025 gus <august.eymann@gmail.com>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Shared.Hands.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Mobs.Components;
using Content.Shared.Physics;
using Content.Shared.Standing;
using Content.Shared.Throwing;
using Content.Shared.Timing;
using Content.Shared.Weapons.Melee;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Containers;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Events;
using Robust.Shared.Physics.Systems;

namespace Content.Shared._Goobstation.Weapons.MeleeDash;

public sealed class MeleeDashSystem : EntitySystem
{
    [Dependency] private readonly UseDelaySystem _useDelay = default!;
    [Dependency] private readonly SharedHandsSystem _hands = default!;
    [Dependency] private readonly SharedContainerSystem _container = default!;
    [Dependency] private readonly ThrowingSystem _throwing = default!;
    [Dependency] private readonly SharedMeleeWeaponSystem _melee = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly StandingStateSystem _standing = default!;
    [Dependency] private readonly SharedPhysicsSystem _physics = default!;

    private const int DashCollisionLayer = (int) CollisionGroup.MidImpassable;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<DashingComponent, LandEvent>(OnLand);
        SubscribeLocalEvent<DashingComponent, StartCollideEvent>(OnCollide);
        SubscribeAllEvent<MeleeDashEvent>(OnDash);
    }

    private void OnCollide(Entity<DashingComponent> ent, ref StartCollideEvent args)
    {
        var (uid, comp) = ent;

        if (!TryComp(comp.Weapon, out MeleeWeaponComponent? melee))
            return;

        if (comp.HitEntities.Contains(args.OtherEntity))
            return;

        if (!HasComp<MobStateComponent>(args.OtherEntity))
            return;

        if (!TryComp(uid, out HandsComponent? hands) || _hands.GetActiveItem((uid, hands)) != comp.Weapon)
            return;

        comp.HitEntities.Add(args.OtherEntity);
        Dirty(ent);

        _melee.AttemptLightAttack(uid, comp.Weapon.Value, melee, args.OtherEntity);
    }

    private void OnLand(Entity<DashingComponent> ent, ref LandEvent args)
    {
        var (uid, comp) = ent;

        if (TryComp(uid, out FixturesComponent? fixtureComponent))
        {
            foreach (var key in comp.ChangedFixtures)
            {
                if (!fixtureComponent.Fixtures.TryGetValue(key, out var fixture))
                    continue;

                _physics.SetCollisionMask(uid,
                    key,
                    fixture,
                    fixture.CollisionMask | DashCollisionLayer,
                    fixtureComponent);
            }
        }

        RemCompDeferred(uid, comp);
    }

    private void OnDash(MeleeDashEvent msg, EntitySessionEventArgs args)
    {
        if (args.SenderSession.AttachedEntity is not { } user)
            return;

        var weapon = GetEntity(msg.Weapon);

        if (!_melee.TryGetWeapon(user, out var weaponUid, out _) || weaponUid != weapon)
            return;

        if (_standing.IsDown(user))
            return;

        if (_container.IsEntityInContainer(user))
            return;

        if (HasComp<DashingComponent>(user))
            return;

        if (!TryComp(weapon, out MeleeDashComponent? dash) ||
            !TryComp(weapon, out UseDelayComponent? delay) ||
            _useDelay.IsDelayed((weapon, delay)))
            return;

        var length = MathF.Min(msg.Direction.Length(), dash.MaxDashLength);
        if (length <= 0f)
            return;

        var dir = msg.Direction.Normalized() * length;

        _useDelay.TryResetDelay((weapon, delay));

        var dashing = EnsureComp<DashingComponent>(user);

        if (TryComp(user, out FixturesComponent? fixtureComponent))
        {
            foreach (var (key, fixture) in fixtureComponent.Fixtures)
            {
                if ((fixture.CollisionMask & DashCollisionLayer) == 0)
                    continue;

                dashing.ChangedFixtures.Add(key);
                _physics.SetCollisionMask(user,
                    key,
                    fixture,
                    fixture.CollisionMask & ~DashCollisionLayer,
                    manager: fixtureComponent);
            }
        }

        dashing.Weapon = weapon;
        Dirty(user, dashing);

        _throwing.TryThrow(user, dir, dash.DashForce, null, 0f, null, false, false, false, false, false);
        _audio.PlayPredicted(dash.DashSound, user, user);
    }
}
