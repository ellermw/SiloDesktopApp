import { forwardRef, useCallback } from "react";
import { useNavigate } from "react-router";
import type { LinkProps } from "react-router";

/**
 * A drop-in replacement for React Router's `<Link>` that wraps
 * navigation in the View Transitions API for smooth page transitions.
 *
 * Falls back to regular link navigation in browsers that don't
 * support the View Transitions API.
 *
 * Renders a standard `<a>` tag and handles navigation programmatically,
 * which avoids depending on `BrowserRouter` to support `viewTransition`.
 */
const ViewTransitionLink = forwardRef<
  HTMLAnchorElement,
  LinkProps & React.AnchorHTMLAttributes<HTMLAnchorElement>
>(function ViewTransitionLink({ to, replace, state, onClick, children, ...rest }, ref) {
  const navigate = useNavigate();

  const handleClick = useCallback(
    (e: React.MouseEvent<HTMLAnchorElement>) => {
      // Call any existing onClick handler first
      onClick?.(e);

      if (e.defaultPrevented) return;

      // Don't intercept modified clicks (new tab, etc.)
      if (e.metaKey || e.ctrlKey || e.shiftKey || e.altKey || e.button !== 0) {
        return;
      }

      e.preventDefault();

      const navOptions = { replace, state };

      if (document.startViewTransition) {
        document.startViewTransition(() => {
          navigate(to, navOptions);
        });
      } else {
        navigate(to, navOptions);
      }
    },
    [navigate, to, replace, state, onClick],
  );

  // Resolve the href string for the <a> tag
  const href = typeof to === "string" ? to : `${to.pathname ?? ""}${to.search ?? ""}${to.hash ?? ""}`;

  return (
    <a ref={ref} href={href} onClick={handleClick} {...rest} data-discover="true">
      {children}
    </a>
  );
});

export default ViewTransitionLink;
